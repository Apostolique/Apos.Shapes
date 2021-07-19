# Apos.Shapes
Shape rendering in MonoGame.

[![Discord](https://img.shields.io/discord/355231098122272778.svg)](https://discord.gg/MonoGame)

## Documentation

* Coming soon!

## Build

[![NuGet](https://img.shields.io/nuget/v/Apos.Shapes.svg)](https://www.nuget.org/packages/Apos.Shapes/) [![NuGet](https://img.shields.io/nuget/dt/Apos.Shapes.svg)](https://www.nuget.org/packages/Apos.Shapes/)

## Usage samples

Install with:

```
dotnet add package Apos.Shapes
```

Add to your Game1.cs:

```csharp
using Apos.Shapes;

// ...

_graphics.GraphicsProfile = GraphicsProfile.HiDef;

// ...

ShapeBatch _sb = new ShapeBatch(GraphicsDevice, Content);

// ...

_sb.Begin();
_sb.DrawCircle(new Vector2(100f, 0f), 100f, Color.Red, Color.White, 4f);
_sb.DrawCircle(new Vector2(100, 100), 50, Color.Red, Color.White, 4f);
_sb.DrawCircle(new Vector2(170, 150), 75, Color.Blue * 0.5f, Color.White, 2f);
_sb.DrawCircle(new Vector2(400, 120), 100, Color.Green, Color.White, 10f);
_sb.End();
```

## Other projects you might like

* [Apos.Gui](https://github.com/Apostolique/Apos.Gui) - UI library for MonoGame.
* [Apos.Input](https://github.com/Apostolique/Apos.Input) - Polling input library for MonoGame.
* [Apos.History](https://github.com/Apostolique/Apos.History) - A C# library that makes it easy to handle undo and redo.
* [Apos.Content](https://github.com/Apostolique/Apos.Content) - Content builder library for MonoGame.
* [Apos.Framework](https://github.com/Apostolique/Apos.Framework) - Game architecture for MonoGame.
* [AposGameStarter](https://github.com/Apostolique/AposGameStarter) - MonoGame project starter. Common files to help create a game faster.
