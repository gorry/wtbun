using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Wtbun.Projects;

/// <summary>
/// タブに出すプロジェクトのアイコン。プロジェクト直下の .wtbun.ico、.wtbun.png の順に探し、どちらもなければ wtbun のアイコン。
/// </summary>
public static class ProjectIcon
{
    public const string IconFileName = $".{AppProfile.ShortTitle}.ico";
    public const string ImageFileName = $".{AppProfile.ShortTitle}.png";

    /// <summary>wtbun のアイコン（exe のアイコンと同じ app.ico）。</summary>
    public static ImageSource Default { get; } = LoadDefault();

    /// <summary>
    /// プロジェクトのアイコン。.wtbun.ico、.wtbun.png の順に、読めた最初のもの。どちらもなければ（読めなければ）wtbun のアイコン。
    /// </summary>
    public static ImageSource Load(string projectDir) =>
        new[] { IconFileName, ImageFileName }
            .Select(name => Path.Combine(projectDir, name))
            .Where(File.Exists)
            .Select(TryLoadImage)
            .FirstOrDefault(image => image is not null)
        ?? Default;

    /// <summary>
    /// 画像ファイルを読む。複数の大きさを持つもの（.ico など）は、いちばん大きいものを使う。読めなければ null。
    /// ファイルを開いたままにしないよう、全部読んでから閉じる。
    /// </summary>
    public static BitmapSource? TryLoadImage(string path)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            return Decode(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
            or FileFormatException or ArgumentException or InvalidOperationException)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    /// <summary>画像の中央から、いちばん大きい正方形を切り抜く。</summary>
    public static BitmapSource CropToSquare(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var size = Math.Min(bgra.PixelWidth, bgra.PixelHeight);
        var rect = new Int32Rect((bgra.PixelWidth - size) / 2, (bgra.PixelHeight - size) / 2, size, size);
        var stride = size * 4;
        var pixels = new byte[stride * size];
        bgra.CopyPixels(rect, pixels, stride, 0);
        // 元の画像の DPI は引き継がない（表示の大きさが画像ごとに変わらないように）
        var square = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        square.Freeze();
        return square;
    }

    /// <summary>
    /// プロジェクト直下に .wtbun.png として保存する（24 ビット RGB ＋ 8 ビットのアルファ）。
    /// 書いている途中で終わっても壊れないよう、別のファイルに書いてから置き換える。
    /// </summary>
    public static void SaveImage(string projectDir, BitmapSource image)
    {
        var path = Path.Combine(projectDir, ImageFileName);
        var temp = path + ".tmp";
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0)));
        using (var stream = File.Create(temp))
        {
            encoder.Save(stream);
        }
        File.Move(temp, path, overwrite: true);
    }

    private static BitmapSource Decode(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.MaxBy(f => f.PixelWidth * f.PixelHeight)
            ?? throw new FileFormatException("画像がありません。");
        frame.Freeze();
        return frame;
    }

    private static ImageSource LoadDefault()
    {
        var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/{typeof(ProjectIcon).Assembly.GetName().Name};component/app.ico"));
        using var stream = resource.Stream;
        return Decode(stream);
    }
}
