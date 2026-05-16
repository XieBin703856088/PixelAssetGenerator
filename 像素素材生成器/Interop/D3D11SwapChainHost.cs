using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace PixelAssetGenerator.Interop
{
    // Hosts a native HWND with a D3D11 swap chain and allows presenting a provided
    // ID3D11Texture2D by copying into the swap-chain back buffer.
    internal sealed class D3D11SwapChainHost : HwndHost, IDisposable
    {
        // Present sync interval: 0 = immediate (no vsync), 1 = vsync. Default to 1 for smooth UI.
        private int _syncInterval = 1;

        public int SyncInterval
        {
            get => _syncInterval;
            set => _syncInterval = value < 0 ? 0 : value;
        }

        private IntPtr _hwnd = IntPtr.Zero;
        private IDXGISwapChain1? _swapChain;
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;

        public D3D11SwapChainHost()
        {
        }

        private int _bufferWidth = 0;
        private int _bufferHeight = 0;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            // Create child HWND
            _hwnd = CreateHostWindow(hwndParent.Handle);

            // Acquire D3D11 device from GpuCompute when available
            try
            {
                _device = Core.Gpu.GpuCompute.GetD3D11DeviceForInterop();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"D3D11SwapChainHost: GetD3D11DeviceForInterop 调用失败: {ex}");
                // preserve original fallback behavior
                _device = null;
            }

            if (_device == null)
            {
                Trace.TraceWarning("D3D11SwapChainHost: 无法从 GpuCompute 获取 D3D11 设备");
                try { System.Diagnostics.Trace.TraceInformation("[调试] D3D11SwapChainHost: 未能从 GpuCompute 获取 D3D11 设备，GPU 直接预览不可用。请检查 GpuCompute 初始化日志。"); } catch { }
                return new HandleRef(this, _hwnd);
            }

            try
            {
                _context = _device.ImmediateContext;
                // Create DXGI factory and swap chain for hwnd using the existing device
                // Create a DXGI factory and swap chain for the HWND. Use CreateDXGIFactory1
                // to obtain a factory compatible with IDXGIFactory2.
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();

                var desc = new SwapChainDescription1()
                {
                    Width = 0,
                    Height = 0,
                    Format = Format.B8G8R8A8_UNorm,
                    Stereo = false,
                    SampleDescription = new SampleDescription(1, 0),
                    BufferCount = 2,
                    // Do not let DXGI scale the buffer — we will match swap-chain buffer
                    // size to the rendered texture and the host HWND size to preserve aspect.
                    Scaling = Scaling.None,
                    SwapEffect = SwapEffect.FlipSequential,
                    AlphaMode = AlphaMode.Ignore
                };

                _swapChain = factory.CreateSwapChainForHwnd(_device, _hwnd, desc);
                try { System.Diagnostics.Trace.TraceInformation($"[调试] D3D11SwapChainHost: 创建 swap chain 成功 (hwnd={_hwnd})"); }
                catch (Exception ex)
                {
                    Trace.TraceError($"D3D11SwapChainHost: Trace.TraceInformation 调用失败: {ex}");
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"D3D11SwapChainHost: 创建交换链失败: {ex}");
                try { System.Diagnostics.Trace.TraceError($"[调试] D3D11SwapChainHost: 创建 swap chain 失败: {ex}"); } catch { }
                throw;
            }

            return new HandleRef(this, _hwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            try { _swapChain?.Dispose(); }
            catch (Exception ex)
            {
                Trace.TraceError($"D3D11SwapChainHost: 释放交换链失败: {ex}");
            }
            _swapChain = null;
            try { _context = null; }
            catch (Exception ex)
            {
                Trace.TraceError($"D3D11SwapChainHost: 清除上下文引用失败: {ex}");
            }
            try { _device = null; }
            catch (Exception ex)
            {
                Trace.TraceError($"D3D11SwapChainHost: 清除设备引用失败: {ex}");
            }
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }

        public void PresentRenderTexture(ID3D11Texture2D? src)
        {
            if (_swapChain == null || _device == null || _context == null || src == null) return;
            try
            {
                try { System.Diagnostics.Trace.TraceInformation("[调试] D3D11SwapChainHost.PresentRenderTexture 调用"); }
                catch (Exception ex)
                {
                    Trace.TraceError($"D3D11SwapChainHost: Trace.TraceInformation 调用失败: {ex}");
                }
                using var back = _swapChain.GetBuffer<ID3D11Texture2D>(0);
                if (back == null) return;

                // Copy resource (requires same device). Ensure buffer size matches source.
                try
                {
                    // 尝试拷贝资源到 swapchain 后备缓冲
                    _context.CopyResource(back, src);
                    try { System.Diagnostics.Trace.TraceInformation("[调试] D3D11SwapChainHost: CopyResource 成功"); }
                    catch (Exception ex)
                    {
                        Trace.TraceError($"D3D11SwapChainHost: Trace.TraceInformation 调用失败: {ex}");
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"D3D11SwapChainHost: CopyResource 调用失败: {ex}");
                    try { System.Diagnostics.Trace.TraceError($"[调试] D3D11SwapChainHost: CopyResource 失败: {ex}"); }
                    catch (Exception traceEx)
                    {
                        Trace.TraceError($"D3D11SwapChainHost: Trace.TraceError 调用失败: {traceEx}");
                    }
                    throw;
                }

                // Use configurable sync interval
                try
                {
                    _swapChain.Present(_syncInterval, PresentFlags.None);
                    try { System.Diagnostics.Trace.TraceInformation($"[调试] D3D11SwapChainHost: Present 调用完成 (syncInterval={_syncInterval})"); }
                    catch (Exception ex)
                    {
                        Trace.TraceError($"D3D11SwapChainHost: Trace.TraceInformation 调用失败: {ex}");
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"D3D11SwapChainHost: Present 调用失败: {ex}");
                    try { System.Diagnostics.Trace.TraceError($"[调试] D3D11SwapChainHost: Present 失败: {ex}"); }
                    catch (Exception traceEx)
                    {
                        Trace.TraceError($"D3D11SwapChainHost: Trace.TraceError 调用失败: {traceEx}");
                    }
                    throw;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"D3D11SwapChainHost: Present 调用失败: {ex}");
                try { System.Diagnostics.Trace.TraceError($"[调试] D3D11SwapChainHost: PresentRenderTexture 捕获异常: {ex}"); }
                catch (Exception traceEx)
                {
                    Trace.TraceError($"D3D11SwapChainHost: Trace.TraceError 调用失败: {traceEx}");
                }
                throw;
            }
        }

        public void EnsureBufferSize(int width, int height)
        {
            if (_swapChain == null) return;
            if (width == _bufferWidth && height == _bufferHeight) return;
            try
            {
                // Resize swap-chain buffers to match requested size. Use 2 buffers and same format.
                _swapChain.ResizeBuffers(2, width, height, Format.B8G8R8A8_UNorm, 0);
                _bufferWidth = width;
                _bufferHeight = height;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"D3D11SwapChainHost: ResizeBuffers 调用失败: {ex}");
                try { System.Diagnostics.Trace.TraceError($"[调试] D3D11SwapChainHost: ResizeBuffers 失败: {ex}"); }
                catch (Exception traceEx)
                {
                    Trace.TraceError($"D3D11SwapChainHost: Trace.TraceError 调用失败: {traceEx}");
                }
                throw;
            }
        }

        public new void Dispose()
        {
            try { DestroyWindowCore(new HandleRef(this, _hwnd)); }
            catch (Exception ex)
            {
                Trace.TraceError($"D3D11SwapChainHost: Dispose 调用失败: {ex}");
                throw;
            }
        }

        // Prevent the hosted HWND from stealing Win32 keyboard focus when clicked.
        // MA_NOACTIVATE (3): don't activate the window; mouse message is still processed normally.
        private const uint WM_MOUSEACTIVATE = 0x0021;
        private static readonly IntPtr MA_NOACTIVATE = new IntPtr(3);

        // WM_SYSKEYDOWN/UP carry Alt-key presses. Forward them to the parent so WPF's
        // accelerator/shortcut processing (which lives in the top-level HWND) can handle them.
        private const uint WM_SYSKEYDOWN = 0x0104;
        private const uint WM_SYSKEYUP = 0x0105;

        private static IntPtr HostWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_MOUSEACTIVATE)
                return MA_NOACTIVATE;

            if (msg == WM_SYSKEYDOWN || msg == WM_SYSKEYUP)
            {
                IntPtr parent = GetParent(hWnd);
                if (parent != IntPtr.Zero)
                    PostMessage(parent, msg, wParam, lParam);
                return IntPtr.Zero;
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        // Keep a static reference so the GC never collects the delegate while the window is alive.
        private static readonly NativeWndProc _hostWndProcDelegate = HostWndProc;

        // Simple native child window creation
        private static IntPtr CreateHostWindow(IntPtr parent)
        {
            const string className = "D3D11SwapChainHostWnd";
            var hInst = Marshal.GetHINSTANCE(typeof(D3D11SwapChainHost).Module);

            var wc = new WNDCLASS
            {
                lpfnWndProc = _hostWndProcDelegate,
                hInstance = hInst,
                lpszClassName = className
            };

            RegisterClass(ref wc);

            // WS_CHILD (0x40000000) | WS_VISIBLE (0x10000000)
            var style = 0x40000000 | 0x10000000; // WS_CHILD | WS_VISIBLE
            var hwnd = CreateWindowEx(0, className, string.Empty, style, 0, 0, 100, 100, parent, IntPtr.Zero, hInst, IntPtr.Zero);
            return hwnd;
        }

        #region Win32 interop
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        private delegate IntPtr NativeWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClass([In] ref WNDCLASS lpWndClass);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASS
        {
            public uint style;
            public NativeWndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        #endregion
    }
}

