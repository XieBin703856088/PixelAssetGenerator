#if VORTICE
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PixelAssetGenerator.Core.Gpu
{
    internal static class GpuCompiler
    {
        [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "D3DCompile")]
        private static extern int D3DCompileNative(
            IntPtr pSrcData,
            IntPtr srcDataSize,
            [MarshalAs(UnmanagedType.LPStr)] string sourceName,
            IntPtr pDefines,
            IntPtr pInclude,
            [MarshalAs(UnmanagedType.LPStr)] string entryPoint,
            [MarshalAs(UnmanagedType.LPStr)] string target,
            uint flags1,
            uint flags2,
            out IntPtr ppCode,
            out IntPtr ppErrorMsgs);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr GetBufferPointerDelegate(IntPtr thisPtr);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr GetBufferSizeDelegate(IntPtr thisPtr);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseDelegate(IntPtr thisPtr);

        // Compile HLSL source to shader bytecode using the system D3DCompile.
        // Returns true and sets bytecode on success; returns false and sets
        // errorMessage when compilation fails.
        public static bool TryCompile(string hlslSource, string entryPoint, string target, out byte[]? bytecode, out string? errorMessage)
        {
            bytecode = null;
            errorMessage = null;

            // Quick diagnostics: check if system d3dcompiler DLL exists (common source of failures)
            try
            {
                var sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "d3dcompiler_47.dll");
                var sysWow64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "d3dcompiler_47.dll");
                Debug.WriteLine($"GpuCompiler: d3dcompiler_47.dll 存在情况: System32={File.Exists(sys32)}, SysWOW64={File.Exists(sysWow64)}; System32路径={sys32}; SysWOW64路径={sysWow64}");
            }
            catch { }

            // D3DCompile expects ANSI-encoded source text. Converting Unicode text to the
            // system ANSI code page while replacing any non-encodable characters with
            // a harmless space reduces the chance of introducing unexpected tokens
            // (e.g. stray '@' reported by the native compiler) caused by encoding
            // mismatches. Keep a copy of the original source for debugging output.
            string originalSource = hlslSource ?? string.Empty;
            byte[] srcBytes;
            string? ansiPreview = null;
            try
            {
                // Use the system ANSI code page but replace characters that cannot be
                // represented with a space character to avoid injecting odd bytes.
                var ansi = System.Text.Encoding.GetEncoding(
                    System.Text.Encoding.Default.CodePage,
                    new System.Text.EncoderReplacementFallback(" "),
                    new System.Text.DecoderReplacementFallback("?")
                );

                // Remove BOM if present
                if (originalSource.Length > 0 && originalSource[0] == '\uFEFF')
                    originalSource = originalSource.Substring(1);

                // Sanitize known problematic characters that can be introduced
                // by templating or encoding mismatches. The native D3D compiler
                // can interpret stray characters (e.g. '@') as tokens and fail
                // compilation. Replace such characters with a space to avoid
                // injecting unexpected tokens into the final ANSI buffer.
                if (originalSource.IndexOf('@') >= 0)
                {
                    try { originalSource = originalSource.Replace('@', ' '); }
                    catch { /* best-effort sanitize; ignore failures */ }
                }

                srcBytes = ansi.GetBytes(originalSource);
                // Keep a decoded preview of the ANSI bytes for debugging when compilation fails
                try { ansiPreview = ansi.GetString(srcBytes); } catch { ansiPreview = null; }
            }
            catch
            {
                // Fallback to ASCII replace-all-non-ASCII-with-space behavior if ANSI encoding fails
                var arr = originalSource.ToCharArray();
                for (int i = 0; i < arr.Length; i++) if (arr[i] > 127) arr[i] = ' ';
                srcBytes = System.Text.Encoding.ASCII.GetBytes(new string(arr));
            }
            var pSrc = Marshal.AllocHGlobal(srcBytes.Length);
            IntPtr codeBlob = IntPtr.Zero;
            IntPtr errorBlob = IntPtr.Zero;
            try
            {
                Marshal.Copy(srcBytes, 0, pSrc, srcBytes.Length);
                // Use a simple source name with extension to improve native error messages
                var sourceName = "Shader.hlsl";
                var hr = D3DCompileNative(pSrc, (IntPtr)srcBytes.Length, sourceName, IntPtr.Zero, IntPtr.Zero, entryPoint, target, 0, 0, out codeBlob, out errorBlob);
                if (hr < 0 || codeBlob == IntPtr.Zero)
                {
                    Debug.WriteLine($"GpuCompiler: D3DCompileNative 调用失败 hr=0x{hr:X}");
                    if (errorBlob != IntPtr.Zero)
                    {
                        try
                        {
                            var vtbl = Marshal.ReadIntPtr(errorBlob);
                            var getPtr = Marshal.ReadIntPtr(vtbl, IntPtr.Size * 3);
                            var getSizePtr = Marshal.ReadIntPtr(vtbl, IntPtr.Size * 4);
                            var getPtrDel = Marshal.GetDelegateForFunctionPointer<GetBufferPointerDelegate>(getPtr);
                            var getSizeDel = Marshal.GetDelegateForFunctionPointer<GetBufferSizeDelegate>(getSizePtr);
                            var msgPtr = getPtrDel(errorBlob);
                            var msgSize = (int)getSizeDel(errorBlob).ToInt64();
                            errorMessage = Marshal.PtrToStringAnsi(msgPtr, msgSize) ?? string.Empty;
                            Debug.WriteLine("GpuCompiler: 编译错误: " + errorMessage);
                        }
                        catch
                        {
                            // ignore errors while reading the error blob
                        }
                    }
                    else
                    {
                        Debug.WriteLine("GpuCompiler: 编译失败但未返回错误信息块");
                    }
                    try
                    {
                        // Write the original (UTF-8) source to a temp file so developers can
                        // inspect the exact text that was passed in (including Unicode).
                        var fileName = Path.Combine(Path.GetTempPath(), $"PixelGen_Shader_Failed_{DateTime.Now:yyyyMMdd_HHmmss_fff}.hlsl");
                        File.WriteAllText(fileName, originalSource, System.Text.Encoding.UTF8);
                        Debug.WriteLine($"GpuCompiler: 已将失败的着色器源码写入: {fileName}");
                        // Also write the ANSI-converted preview so we can spot encoding-introduced tokens
                        if (!string.IsNullOrEmpty(ansiPreview))
                        {
                            try
                            {
                                var ansiName = Path.Combine(Path.GetTempPath(), $"PixelGen_Shader_Failed_ANSI_{DateTime.Now:yyyyMMdd_HHmmss_fff}.hlsl");
                                File.WriteAllText(ansiName, ansiPreview, System.Text.Encoding.UTF8);
                                Debug.WriteLine($"GpuCompiler: 已将ANSI转换后的着色器预览写入: {ansiName}");
                            }
                            catch { }
                        }
                        if (!string.IsNullOrEmpty(errorMessage))
                        {
                            Debug.WriteLine($"GpuCompiler: 编译错误信息已打印在上方，请查看文件: {fileName}");
                        }
                    }
                    catch { }
                    return false;
                }

                // Extract bytecode from blob
                var blobVTable = Marshal.ReadIntPtr(codeBlob);
                var getBufPtr = Marshal.ReadIntPtr(blobVTable, IntPtr.Size * 3);
                var getBufSizePtr = Marshal.ReadIntPtr(blobVTable, IntPtr.Size * 4);
                var getBufPtrDel = Marshal.GetDelegateForFunctionPointer<GetBufferPointerDelegate>(getBufPtr);
                var getBufSizeDel = Marshal.GetDelegateForFunctionPointer<GetBufferSizeDelegate>(getBufSizePtr);
                var dataPtr = getBufPtrDel(codeBlob);
                var dataSize = (int)getBufSizeDel(codeBlob).ToInt64();
                bytecode = new byte[dataSize];
                Marshal.Copy(dataPtr, bytecode, 0, dataSize);

                // Release blob
                try
                {
                    var releasePtr = Marshal.ReadIntPtr(blobVTable, IntPtr.Size * 2);
                    var releaseDel = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(releasePtr);
                    releaseDel(codeBlob);
                }
                catch
                {
                    // ignore
                }

                Debug.WriteLine($"GpuCompiler: 编译成功，字节码大小={bytecode?.Length}");
                return true;
            }
            finally
            {
                if (errorBlob != IntPtr.Zero)
                {
                    try
                    {
                        var vtbl = Marshal.ReadIntPtr(errorBlob);
                        var releasePtr = Marshal.ReadIntPtr(vtbl, IntPtr.Size * 2);
                        var releaseDel = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(releasePtr);
                        releaseDel(errorBlob);
                    }
                    catch { }
                }

                if (pSrc != IntPtr.Zero)
                    Marshal.FreeHGlobal(pSrc);
            }
        }
    }
}
#endif
