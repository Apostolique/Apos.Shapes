# Getting started

## Install

Install using the following dotnet command:

```
dotnet add package Apos.Shapes
```

## Compilation

This library includes a shader that gets compiled along your other content. If you are running on Linux or OSX, make sure that you have set up Wine correctly or you will get a build error. MonoGame has guides on setting up Wine for both. Make sure to read them:
* [Linux](https://docs.monogame.net/articles/getting_started/1_setting_up_your_os_for_development_ubuntu.html#setup-wine-for-effect-compilation)
* [OSX](https://docs.monogame.net/articles/getting_started/1_setting_up_your_os_for_development_macos.html#setup-wine-for-effect-compilation)

## Setup

Import the library with:

```csharp
using Apos.Shapes;
```

In your game's constructor, set the GraphicsProfile to `HiDef`. (If you're using this library with [KNI](https://github.com/kniengine/kni), set it to `FL10_0` instead.)

```csharp
public Game1() {
    _graphics = new GraphicsDeviceManager(this);

    _graphics.GraphicsProfile = GraphicsProfile.HiDef;
}
```

In your game's `LoadContent()`, create a ShapeBatch instance:

```csharp
protected override void LoadContent() {
    _sb = new ShapeBatch(GraphicsDevice, Content);
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
