using System.Numerics;
using Arena.Engine;
using Raylib_cs;

namespace Arena.Rendering;

/// <summary>Uploads the exact RGB24 observation bytes; never renders a second camera view.</summary>
internal sealed class ObservationTexture : IDisposable
{
    private Texture2D texture;
    private RgbImage? uploaded;

    public void Draw(RgbImage image, Rectangle destination)
    {
        if (texture.Id == 0 || texture.Width != image.Width || texture.Height != image.Height)
        {
            Dispose();
            var blank = Raylib.GenImageColor(image.Width, image.Height, Color.Black);
            try
            {
                Raylib.ImageFormat(ref blank, PixelFormat.UncompressedR8G8B8);
                texture = Raylib.LoadTextureFromImage(blank);
                Raylib.SetTextureFilter(texture, TextureFilter.Point);
            }
            finally { Raylib.UnloadImage(blank); }
        }
        if (!ReferenceEquals(uploaded, image))
        {
            Raylib.UpdateTexture<byte>(texture, image.Pixels.Span);
            uploaded = image;
        }
        Raylib.DrawTexturePro(texture, new Rectangle(0, 0, image.Width, image.Height), destination,
            Vector2.Zero, 0, Color.White);
    }

    public void Dispose()
    {
        if (texture.Id != 0) Raylib.UnloadTexture(texture);
        texture = default;
        uploaded = null;
    }
}
