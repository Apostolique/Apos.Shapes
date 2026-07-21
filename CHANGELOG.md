# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Dashed outlines and strokes. Every `Draw` and `Border` method except the ellipse's (plus `Fill` on the stroke shapes) takes a `DashStyle(size, spacing, offset, cap, snap)`. Closed outlines (circle, rectangle, hexagon, equilateral triangle, triangle) dash their border along the perimeter, corners and rounded corners included, and the gaps show the fill. Strokes (line, arc, ring, path) are cut into dashes along their centerline, so every dash keeps its own fill, border, and anti-aliasing. The pattern rounds every corner it walks. Dashes bend around joints and corners at full width with their edges perpendicular to the flow, compressing on the inside of the bend and stretching on the outside, and they slide off the end caps, all without popping when the offset animates. Miter and bevel tips appear once a dash covers the whole corner and stay rounded otherwise. `DashCap.Butt` cuts dashes flat, `DashCap.Round` gives them round caps that merge seamlessly with round line caps. With size 0 they become dots. By default the pattern is fitted to the contour: closed outlines wrap without a seam and open strokes center a dash on each endpoint (`DashSnap` selects other fits or none). `DashStyle.FromCount(count, fill)` lays a whole number of repeats instead of world unit lengths, so the dashes stretch continuously as the shape changes size and the pattern never pops. Ellipses don't dash yet: their perimeter has no closed form.
- Closed paths. `DrawPath`, `FillPath`, and `BorderPath` take `closed: true`, and the streaming API gained `ClosePath()` alongside `EndPath()`, to join the last point back to the first. The wrap becomes an ordinary joint rather than two caps, so it takes the same round, miter, and bevel styles, a translucent loop still blends exactly once all the way around, and the cap styles go unused. A dash pattern fitted to a closed path wraps without a seam, which is also how shapes the shader can't walk get dashed at all: flatten an ellipse or a curve to a closed path and it dashes like any other loop.

## [0.7.4] - 2026-07-19

### Added

- Gradient banding dither. (#25) Shape colors get half an 8-bit step of screen-space noise before quantization, which dissolves the bands slow gradients produce on 8-bit render targets, from color and alpha gradients alike. The noise pattern is static and imperceptible at the default strength, whether a gradient moves across the screen or holds still. `DitherStrength` on the ShapeBatch scales it in 8-bit steps (0 disables it), and `DitherNoiseSource` selects the pattern: an embedded 64x64 blue noise tile by default, or `InterleavedGradient` computed in the shader. Both cost the same on the GPU.
- The example gained a banding showcase scene on the Tab key: a night sky built from slow dark gradients, with Space cycling the dither mode and Up/Down exaggerating the strength.

## [0.7.3] - 2026-07-18

### Added

- Paths. `DrawPath`, `FillPath`, and `BorderPath` stroke a polyline as a single continuous shape with a fill and a border. Joints can be round, miter, or bevel, either for the whole path or per point using `PathPoint`, and the ends can be capped round, butt, or square, with an optional different cap for each end. Miters respect a miter limit and fall back to bevel past it, like SVG. There's also a streaming API, `BeginPath`/`PathTo`/`EndPath`, to feed points one at a time without building an array first.

### Changed

- Anti-aliasing is now computed per pixel in the shader from screen-space derivatives instead of a per-shape pixel size. Edges stay crisp and consistent under any view matrix, including anisotropic scale, skew, and perspective.
- Hollow shapes now rasterize only their visible band instead of their full bounding quad. Rings, arcs, and any shape drawn with a transparent fill and a border emit a mesh with a hole in the middle, so big outlines no longer pay fill rate for their interior, and arcs no longer cover angles they don't span. Small shapes keep the single quad. Rendering is unchanged.

## [0.7.2] - 2026-07-18

### Changed

- The shader is now precompiled with [ShadowDusk](https://github.com/kaltinril/ShadowDusk) when the library is packed and embedded in the assembly. It is no longer added to your content pipeline, which means building your game no longer requires a shader compiler, and no longer requires Wine on Linux and macOS.
- New `ShapeBatch(GraphicsDevice, Effect?)` constructor. The `ContentManager` overload still works but is obsolete since the content pipeline is no longer used.
- The minimum supported MonoGame version is now 3.8.2. On KNI, the DirectX backends load a standard MGFX effect while the GL family (desktop GL, GLES, WebGL) loads a knifx effect; both are embedded.
- The `SkipAposShapeContent` MSBuild property is gone along with the `buildTransitive` content.

## [0.7.1] - 2026-07-18

### Added

- GPU clipping rect. `SetClipRect` clips upcoming draws to a rectangle without breaking the batch. The clip rectangle supports rounded corners, rotation, and an anti-aliased edge.
- Per-corner radii for rectangles. `DrawRectangle`, `FillRectangle`, and `BorderRectangle` take a `CornerRadii` which allows a different rounding for each corner. A single float still works for uniform rounding.
- `ColorSpace` property on the ShapeBatch. It selects the color space that gradient and border colors are interpolated in. `Oklab` is the default, `Oklch` keeps colors vivid, `Rgb` interpolates the raw channels. It's captured per shape so it can change mid batch without breaking it.
- Spiral gradient shapes. `SpiralCW` winds clockwise around the first point, `SpiralCCW` winds counterclockwise.
- `Color` implicitly converts to `Gradient` and `float` implicitly converts to `CornerRadii` which simplifies the draw call overloads.
- `ShapeBatch` now implements `IDisposable` and disposes its vertex and index buffers.
- `Begin` and `End` now throw when called out of order, and drawing before `Begin` throws instead of silently using stale states.

### Fixed

- On macOS OpenGL, a packed 0 byte could decode as ~255 in the shader which corrupted colors. (#33)
- Drawing a line with the same start and end positions passed the anti-aliasing size as the circle's rotation.
- Gradient offsets no longer divide by zero when both gradient positions are the same.
- A transparent color in a gradient no longer tints the other color during the transition.
- The seam on conical and repeating gradients is now anti-aliased correctly.
- The anti-aliasing between the fill and the border now blends the same way as the shape's outside edge.

### Optimized

- Improved the clip space and optimized the batcher.

## [0.6.8] - 2026-02-28

### Fixed

- The license file is now included in the NuGet package.

## [0.6.7] - 2026-02-28

### Added

- It's now possible to draw gradients in the shape's local space.
- Added the SpriteBatch texture API to the ShapeBatch. It's now possible to draw textures along with shapes without breaking the batch. The draw calls are backed by a Matrix3x2 which supports more drawing options than what the SpriteBatch provides.
- Added the [FontStashSharp](https://github.com/FontStashSharp/FontStashSharp) API which makes it possible to draw text natively. The texture for the font uses a separate texture slot which makes it possible to draw text without breaking the batch.
- It's now possible to pass the BlendState, SamplerState, DepthStencilState, and RasterizerState to the Begin call.
- The GraphicsDevice is now made available.

## [0.5.2] - 2025-12-27

### Added

- It's now possible to skip the automatic shader build by setting `<SkipAposShapeContent>true</SkipAposShapeContent>` in your game's .csproj.

### Fixed

- The projection matrix was using the wrong viewport values. It would mess up split screen rendering.

## [0.5.1] - 2025-12-18

### Added

- Gradient offsets for the first and second colors. This allows you to start a color as a solid color within the offset before transitioning to the other color.

### Fixed

- The anti-aliasing blur should look better. It had been made to be a linear blur in version 0.5.0 but it's now back to using a smoothstep function.

## [0.5.0] - 2025-10-15

### Changed

- Colors are no longer using pre-multipled alpha. This is because for transparent values, the gradient interpolation code needed to have the full color values. If you want transparent white for example, you can pass `new Color(255, 255, 255, 0)` which was impossible when using pre-multipled alpha. This only matters for the colors that are being passed in. You can then do: `new Color(Color.White, 0.5f)` instead of `Color.White * 0.5f`.
- The default anti-aliasing value is now set to 1.5 instead of 2. It should make the shapes look slightly less blurry while still having a nice edge.
- Updated to .NET 9 and MonoGame 3.8.4.

### Added

- Gradients. They come in multiple shapes (linear, radial, conical, and more) and repeat styles. The colors are interpolated in the Oklab color space which avoids muddy transitions.
- Ring shape.
- KNI support.
- You can now pass the shader manually to the ShapeBatch constructor.

### Fixed

- The border and fill color used to overlap. It would look bad when using a transparent border color.
- The arc and ring angles were wrong. (#16)

### Optimized

- Lines that have the same start and end positions are drawn as a circle.

## [0.3.2] - 2025-07-12

### Added

- Arc shape.

## [0.3.1] - 2025-04-12

### Fixed

- Fixed compatibility issue with MonoGame 3.8.3. That MonoGame version has a regression that prevented creating the IndexBuffer using `typeof(uint)`.

## [0.3.0] - 2024-06-06

### Added

- It is now possible to set the anti-aliasing size for each draw call. This controls a sort of blur that helps make shapes smoother. The default value is 2f, it's possible to reduce this in order to draw thinner lines.

## [0.2.4] - 2024-04-11

### Fixed

- The viewport value wasn't used correctly for the projection matrix which prevented doing split screens.

### Added

- Triangle shape. Allows defining a triangle from three points.

## [0.2.3] - 2023-11-29

### Added

- Ellipse shape.

## [0.2.2] - 2023-11-23

### Fixed

- The filled shapes had a border when the color was transparent.

### Changed

- Adjusted the overlap between the border and fill. The border has slightly less anti-aliasing.

## [0.2.1] - 2023-11-23

### Changed

- Adjusted the border thickness. It should be more accurate.

## [0.2.0] - 2023-11-10

### Added

- Added equilateral triangle shape.
- Added rounded API for rectangle, hexagon, triangle shapes.
- Added rotation API for rectangle, hexagon, triangle shapes.

### Changed

- The way the border is drawn is slightly different than before. In general borders will appear slightly thicker but will have a more accurate color and size.
- Border thickness is now in world scale. In the previous version, borders were defined in screen scale which meant that they remained the same size no matter the view matrix.

## [0.1.10] - 2023-08-22

### Fixed

- Bug where resizing the batch more than twice on the same frame would prevent the index and vertex buffers from being resized correctly.

## [0.1.9] - 2023-03-09

### Fixed

- Bug where the floating point comparison used in the shader could fail on some GPUs ending up with the wrong shape.

## [0.1.8] - 2023-02-09

### Optimized

- The shape batch now resizes itself. This makes it be faster based on my tests.

## [0.1.7] - 2022-04-16

### Added

- New hexagon shape.

## [0.1.6] - 2021-12-12

### Fixed

- Compatibility issue with the [MonoGame Compute fork](https://github.com/cpt-max/Docs/blob/master/Build%20Requirements.md).

## [0.1.5] - 2021-09-05

### Fixed

- Border without a fill didn't have the right thickness.

## [0.1.4] - 2021-08-17

### Changed

- The Fill methods have been renamed to Draw. FillCircle becomes DrawCircle. The Draw methods are used to draw a shape with both a fill and a border.

### Added

- Fill methods draw a shape without a border.
- Border methods draw a shape without a fill. A border encases a shape without going outside it's boundaries.

## [0.1.3] - 2021-07-25

### Added

- Line segments. The end caps are rounded.

### Fixed

- Anti-aliasing between main color and border color

## [0.1.2] - 2021-07-20

### Fixed

- Shapes weren't drawn at the correct position

### Optimized

- The ShapeBatch should be slightly faster

## [0.1.1] - 2021-07-19

### Added

- Rectangle

## [0.1.0] - 2020-07-08

### Added

- Everything!

[Unreleased]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.4...HEAD
[0.7.4]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.3...v0.7.4
[0.7.3]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.2...v0.7.3
[0.7.2]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.1...v0.7.2
[0.7.1]: https://github.com/Apostolique/Apos.Shapes/compare/v0.6.8...v0.7.1
[0.6.8]: https://github.com/Apostolique/Apos.Shapes/compare/v0.6.7...v0.6.8
[0.6.7]: https://github.com/Apostolique/Apos.Shapes/compare/v0.5.2...v0.6.7
[0.5.2]: https://github.com/Apostolique/Apos.Shapes/compare/v0.5.1...v0.5.2
[0.5.1]: https://github.com/Apostolique/Apos.Shapes/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/Apostolique/Apos.Shapes/compare/v0.3.2...v0.5.0
[0.3.2]: https://github.com/Apostolique/Apos.Shapes/compare/v0.3.1...v0.3.2
[0.3.1]: https://github.com/Apostolique/Apos.Shapes/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/Apostolique/Apos.Shapes/compare/v0.2.4...v0.3.0
[0.2.4]: https://github.com/Apostolique/Apos.Shapes/compare/v0.2.3...v0.2.4
[0.2.3]: https://github.com/Apostolique/Apos.Shapes/compare/v0.2.2...v0.2.3
[0.2.2]: https://github.com/Apostolique/Apos.Shapes/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/Apostolique/Apos.Shapes/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.10...v0.2.0
[0.1.10]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.9...v0.1.10
[0.1.9]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.8...v0.1.9
[0.1.8]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.7...v0.1.8
[0.1.7]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.6...v0.1.7
[0.1.6]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.5...v0.1.6
[0.1.5]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.4...v0.1.5
[0.1.4]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.3...v0.1.4
[0.1.3]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Apostolique/Apos.Shapes/releases/tag/v0.1.0
