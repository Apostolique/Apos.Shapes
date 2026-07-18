# Text
This guide will show you how to draw text with the ShapeBatch.

The ShapeBatch implements the [FontStashSharp](https://github.com/FontStashSharp/FontStashSharp) API. FontStashSharp comes with the library, there is nothing extra to install. The font texture uses a separate texture slot which makes it possible to draw text along with shapes without breaking the batch.

## Load a font

Put a ttf font file in your Content folder and make sure it gets copied to the output directory. In your game's `LoadContent()`, create a `FontSystem` and add the font to it:

```csharp
using FontStashSharp;
```

```csharp
protected override void LoadContent() {
    _sb = new ShapeBatch(GraphicsDevice);

    _fontSystem = new FontSystem();
    _fontSystem.AddFont(TitleContainer.OpenStream($"{Content.RootDirectory}/my-font.ttf"));
}

ShapeBatch _sb;
FontSystem _fontSystem;
```

You can add multiple fonts to the same `FontSystem`. The extra fonts are used as fallbacks when a character is missing from the first one.

## Draw text

In your draw loop, get a font at the size that you want and draw it between `Begin` and `End`:

```csharp
protected override void Draw(GameTime gameTime) {
    GraphicsDevice.Clear(Color.Black);

    var font = _fontSystem.GetFont(24);

    _sb.Begin();
    _sb.FillCircle(new Vector2(120, 120), 75, new Color(96, 165, 250));
    _sb.DrawString(font, "Hello!", new Vector2(100, 100), Color.White);
    _sb.End();

    base.Draw(gameTime);
}
```

Shapes and text are drawn in the order that you call them. `DrawString` also takes optional parameters for rotation, origin, scale, character spacing, line spacing, text styles, and effects.

## Follow up

[Textures](./textures.md), a guide that shows how to draw textures with the ShapeBatch.
