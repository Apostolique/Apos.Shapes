# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Nothing yet!

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

[Unreleased]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.5...HEAD
[0.1.5]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.4...v0.1.5
[0.1.4]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.3...v0.1.4
[0.1.3]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/Apostolique/Apos.Shapes/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Apostolique/Apos.Shapes/releases/tag/v0.1.0
