using System.Windows.Media.Imaging;

namespace PixelAssetGenerator.Generators
{
    public interface ITileGenerator
    {
        BitmapSource Generate(int size, TileLayerSettings settings);
    }
}
