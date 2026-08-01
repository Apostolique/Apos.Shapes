# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

- Nothing yet!

## [0.7.14] - 2026-08-01

### Fixed

- A `ColorRamp` drawn in `Oklch` painted a one pixel line of the opposite color wherever its hue crossed the wheel.

## [0.7.13] - 2026-08-01

### Added

- Chamfers. `DrawChamfer`, `FillChamfer`, and `BorderChamfer` draw a rectangle whose corners are cut straight across. A `CornerChamfers` sets how far back each corner is cut. They take gradients, dashes, clipping, and rotation like every other shape.
- Palettes. A `Gradient` takes a `Palette` in place of its two stop colors: `bias + amplitude * cos(tau * (frequency * t + phase))` per channel, so one gradient runs through many colors. Takes every gradient shape, repeat style, offset, local space, and color space, and tiles with no seam on `Sawtooth`.
- `Palette.FromStops`, which fits a palette through color stops instead of cosine parameters.
- Ramps. A `Gradient` takes a `Ramp`, a curve of `(position, value)` stops that reshapes how it travels between its colors. Two stops on one position make a hard edge, antialiased like a shape edge, so a two stop gradient or a palette cuts into bands at arbitrary positions.
- Color ramps. A `Gradient` takes a `ColorRamp`, `(position, color)` stops in place of its two stop colors, so one gradient runs through as many colors as you want. Two stops on one position make a hard edge, antialiased like a shape edge. Takes every gradient shape, repeat style, offset, local space, and color space, and the fill and border each take their own.
- Ramps and color ramps can be rebuilt every frame, so their stops animate. Past 256 distinct ones in a batch, the batch flushes early to make room.
- `FillChamferBlurred` and `BorderChamferBlurred`.
- `Measure.Chamfer` and `Measure.ChamferBlurred`.
- XML documentation on the whole public API, so IntelliSense shows a summary and parameter help on every call.

### Changed

- `FillEllipse`, `BorderEllipse`, `FillEllipseBlurred`, `BorderEllipseBlurred`, and `Measure.EllipseBlurred` renamed their `width` and `height` parameters to `radius1` and `radius2`, matching `DrawEllipse`.

### Fixed

- A round capped dash on a rectangle, hexagon, triangle, or chamfer had its cap notched on one side and whiskered on the other when the dash ended on a corner. Dots came out the same way.
- A butt capped dash long enough to turn more than a right angle (one triangle corner, two rectangle or hexagon corners, three chamfer vertices, half a circle, or a sharp path joint) had a wedge bitten out of it, and a gap turning that far kept a spur of border.

## [0.7.12] - 2026-07-27

### Added

- `Measure`, the bounds a shape covers: `Measure.Circle`, `Rectangle`, `Line`, `Path`, `StyledPath`, `Hexagon`, `EquilateralTriangle`, `Triangle`, `Ellipse`, `Arc`, and `Ring`.
- `Measure.CircleBlurred`, `EllipseBlurred`, `RectangleBlurred`, and `LineBlurred`, which cover the whole falloff.

## [0.7.11] - 2026-07-26

### Added

- Paths that vary in width. `DrawPath`, `FillPath`, `BorderPath`, and `PathTo` take a radius per point. Dashes come out as wide as the stroke is where they land.
- `FillLineBlurred` and `BorderLineBlurred`, which take one radius or one per end.
- `AAStyle`, which sets where a shape's anti-aliasing edge sits. `Centered` draws a shape at exactly its size. The default, `Outside`, draws as before.

### Fixed

- Arcs and rings were drawn one pixel smaller than the radius they were given.
- A ring's second radius is the band's half thickness now, matching an arc's. Rings come out twice as thick as before.
- A ring drawn as a full turn had a seam across it.

### Optimized

- The shader is a third smaller and compiles about 40% faster.
- Gradients cost less per pixel, linear and bilinear ones most of all.
- Dashed ellipses cost less per pixel.
- Shapes build two to three times faster, and solid shapes in the default color space several times faster.
- Paths build about 1.5 times faster, about twice as fast when their joints are shallow, and one longer than 256 points no longer allocates.

## [0.7.10] - 2026-07-25

### Added

- Blurred shapes. `FillCircleBlurred`, `FillEllipseBlurred`, `FillRectangleBlurred` and a `Border` version of each fill a flat color with a Gaussian edge, measured in world units. See the [blur guide](docs/blur/README.md).

### Fixed

- The OpenGL shader still failed to load on macOS with "Shader Compilation Failed", and in the browser through KNI's WebGL.

## [0.7.9] - 2026-07-25

### Added

- Dashed ellipses. `DrawEllipse` and `BorderEllipse` take a `DashStyle`, so every shape dashes now.

### Fixed

- A dashed path drew a hairline sliver of the wrong color near a joint, at the phases that put a dash edge close to the corner.

## [0.7.8] - 2026-07-24

### Fixed

- The OpenGL shader failed to load on macOS with "Shader Compilation Failed".
- On OpenGL, the tips of thin ellipses were missing and ellipse edges anti-aliased too light.

## [0.7.7] - 2026-07-23

### Added

- Support for MonoGame 3.8.5's new preview WindowsDX12 (DirectX 12) platform. The DirectX 12 shader variant is embedded alongside the others and picked automatically.

## [0.7.6] - 2026-07-22

### Added

- Support for MonoGame 3.8.5's new preview DesktopVK (Vulkan) platform. The Vulkan shader variant is embedded alongside the others and picked automatically.

## [0.7.5] - 2026-07-21

### Added

- Dashed outlines and strokes. Every `Draw` and `Border` method except the ellipse's, plus `Fill` on the stroke shapes, takes a `DashStyle(size, spacing, offset, cap, snap)`. Closed outlines dash their border along the perimeter, strokes are cut into dashes along their centerline. `DashCap.Round` gives each dash a round cap, and with a size of 0 they become dots. `DashSnap` fits the pattern to the contour, and `DashStyle.FromCount(count, fill)` lays a whole number of repeats instead of world unit lengths. Ellipses don't dash yet. See the [dash guide](docs/dashes/README.md).
- Closed paths. `DrawPath`, `FillPath`, and `BorderPath` take `closed: true`, and the streaming API gained `ClosePath()` alongside `EndPath()`, to join the last point back to the first. The wrap becomes an ordinary joint rather than two caps.

## [0.7.4] - 2026-07-19

### Added

- Gradient banding dither. (#25) Shape colors get half an 8-bit step of screen-space noise before quantization, which dissolves the bands slow gradients produce on 8-bit render targets. `DitherStrength` on the ShapeBatch scales it in 8-bit steps (0 disables it), and `DitherNoiseSource` picks between an embedded blue noise tile and `InterleavedGradient`. See the [gradients guide](docs/gradients/README.md).

## [0.7.3] - 2026-07-18

### Added

- Paths. `DrawPath`, `FillPath`, and `BorderPath` stroke a polyline as a single continuous shape with a fill and a border. Joints can be round, miter, or bevel, either for the whole path or per point using `PathPoint`, and the ends can be capped round, butt, or square. There's also a streaming API, `BeginPath`/`PathTo`/`EndPath`, to feed points one at a time without building an array first.

### Changed

- Anti-aliasing is now computed per pixel in the shader from screen-space derivatives instead of a per-shape pixel size. Edges stay crisp under any view matrix, including anisotropic scale, skew, and perspective.

### Optimized

- Hollow shapes now rasterize only their visible band instead of their full bounding quad, so big outlines no longer pay fill rate for their interior. Rendering is unchanged.

## [0.7.2] - 2026-07-18

### Changed

- The shader is now precompiled with [ShadowDusk](https://github.com/kaltinril/ShadowDusk) and embedded in the assembly. It's no longer added to your content pipeline, so building your game no longer needs a shader compiler, or Wine on Linux and macOS.
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

[Unreleased]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.14...HEAD
[0.7.14]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.13...v0.7.14
[0.7.13]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.12...v0.7.13
[0.7.12]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.11...v0.7.12
[0.7.11]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.10...v0.7.11
[0.7.10]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.9...v0.7.10
[0.7.9]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.8...v0.7.9
[0.7.8]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.7...v0.7.8
[0.7.7]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.6...v0.7.7
[0.7.6]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.5...v0.7.6
[0.7.5]: https://github.com/Apostolique/Apos.Shapes/compare/v0.7.4...v0.7.5
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
