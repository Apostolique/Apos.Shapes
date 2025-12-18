# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

- Nothing yet!

## [0.5.1] - 2025-12-18

### Added

- Gradient offsets for the first and second colors. This allows you to start a color as a solid color within the offset before transitioning to the other color.

### Fixed

- The anti-aliasing blur should look better. It had been made to be a linear blur in version 0.5.0 but it's now back to using a smoothstep function.

### Optimized

- Simplified the gradient code a little bit.

## [0.5.0] - 2025-10-15

### Changed

- Colors are no longer using pre-multipled alpha. This is because for transparent values, the gradient interpolation code needed to have the full color values. If you want transparent white for example, you can pass `new Color(255, 255, 255, 0)` which was impossible when using pre-multipled alpha. This only matters for the colors that are being passed in. You can then do: `new Color(Color.White, 0.5f)` instead of `Color.White * 0.5f`.
- The default anti-aliasing value is now set to 1.5 instead of 2. It should make the shapes look slightly less blurry while still having a nice edge.
- Anti-aliasing is now done using the gradient code. It makes the code be simpler.

### Added

- Gradients
- Ring shape.
- You can now pass the shader manually to the ShapeBatch constructor.

### Fixed

- The border and fill color used to overlap. It would look bad when using a transparent border color.

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

### Optimized

- Simplified the ShapeVertex which should made the library be easier to maintain.

### Added

- Triangle shape. Allows defining a triangle from three points.

## [0.2.3] - 2023-11-29

### Added

- Ellipse shape.

## [0.2.2] - 2023-11-23

### Fixed

- The filled shapes had a border when the color was transparent.

### Changed

- Adjusted the border thickness. It should be more accurate.

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

### Optimized

- Simplified the math a bit to make the library easier to maintain. It should help add more shapes in the future.

## [0.1.10] - 2023-08-22

### Fixed

- Bug where resizing the batch more than twice on the same frame would prevent the index and vertex buffers from being resized correctly.

## [0.1.9] - 2023-03-09

### Fixed

- Bug where the floating point comparison used in the shader could fail on some GPUs ending up with the wrong shape.

### Changed

- The shape shader effect has been internally rename to `apos-shapes.fx` from `apos-shapes-effect.fx`.

## [0.1.8] - 2023-02-09

### Optimized

- The shape batch now resizes itself. This makes it be faster based on my tests.

### Changed

- The shape shader effect has been internally rename to `apos-shapes-effect.fx` from `AposShapesEffect.fx`.

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

[Unreleased]: https://github.com/Apostolique/Apos.Shapes/compare/v0.5.1...HEAD
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
[0.1.8]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.7...v0.1.8
[0.1.7]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.6...v0.1.7
[0.1.6]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.5...v0.1.6
[0.1.5]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.4...v0.1.5
[0.1.4]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.3...v0.1.4
[0.1.3]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Apostolique/Apos.Shapes/releases/tag/v0.1.0
