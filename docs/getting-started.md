# Getting started
This guide will show you how to get started with the Apos.Shapes library.

Before you start, make sure that you have a valid MonoGame project. You can create a new project by following this [other guide](https://learn-monogame.github.io/how-to/get-started/).

You can run this example directly in your browser [here](https://xnafiddle.net/#code=H4sIAAAAAAAAA41S20rEMBB9X9h_mMcUllAFXxQf9uINLIi66puk6Ww7GJOSpFtU_HebWrsXF3ZDoe3MnDNzTqZypHN4-HAe38-Gg6r9TUha48zC8xct-KUV71gb-7Yvz6-sKAuSri8cl8bxh0KUGGLDQVmliiRIJZyD5OOqAcIptK-v4QCa80cxwyVJTIQWOVp4zVfMoaqlnAgvC3h1aUsdwh39LzGL_jjD6RngHDTWu_swX5CLznageml31ixIYcOyFeHXNMPFGvbGJaZy-ESO0hbgbYVr-WfSman5WClTzx3ae3T0GUzbqPzutVnjUXrMwCzRWsoQloYyuNHkSSj63BKcCod8PXko4a0R2dRoj9pvWejSzryV_WzTxxF0yIO7zctMeGThwh6pWYO8-2g67wfPrKh3QldDb87HpwqFZVOjjOUTJeRb1C9PJ5FPMCfN_scvSakpWamQBQ-emoGMPWbHcTyCoziORnASB_2B-rkgj_8pLnTG1tervaNWRD_7yrfm-QEZrfGonQMAAA).

## Install

Install using the following dotnet command:

```
dotnet add package Apos.Shapes
```

You can find other ways to install it on the [NuGet page](https://www.nuget.org/packages/Apos.Shapes/).

The library ships with a precompiled shader that is embedded in its assembly. Nothing gets added to your content pipeline and no shader compiler (or Wine on Linux / macOS) is needed. The minimum supported MonoGame version is 3.8.2.

## Setup

Import the library with:

```csharp
using Apos.Shapes;
```

In your game's constructor, set the GraphicsProfile to `HiDef`. (If you're using this library with [KNI](https://github.com/kniengine/kni), set it to `FL10_0` instead.) This enables the features that the shader will use. By default, MonoGame uses the `Reach` profile.

```csharp
public Game1() {
    _graphics = new GraphicsDeviceManager(this);

    _graphics.GraphicsProfile = GraphicsProfile.HiDef;
}
```

In your game's `LoadContent()`, create a ShapeBatch instance:

```csharp
protected override void LoadContent() {
    _sb = new ShapeBatch(GraphicsDevice);
}

ShapeBatch _sb;
```

In your game's draw loop, call `Begin` and `End` and do your drawing between those two calls:

```csharp
protected override void Draw(GameTime gameTime) {
    GraphicsDevice.Clear(Color.Black);

    _sb.Begin();

    _sb.FillCircle(new Vector2(200, 100), 50, Color.White);

    _sb.End();

    base.Draw(gameTime);
}
```

Everything that is drawn within the `Begin` and `End` calls will be batched together.

## Follow up

[Shapes](./shapes.md), a page that lists every shape that the ShapeBatch can draw.

