# Shapes
This page lists every shape that the ShapeBatch can draw. If you don't have a ShapeBatch set up yet, read the [Getting started](../getting-started/README.md) guide first.

## Naming convention

Every shape comes with three methods:

* `Fill` draws a shape without a border.

  ![A circle with only a fill](fill.png)

* `Border` draws a border without a fill. A border encases a shape without going outside its boundaries.

  ![A circle with only a border](border.png)

* `Draw` draws a shape with both a fill and a border.

  ![A circle with both a fill and a border](draw.png)

```csharp
_sb.FillCircle(new Vector2(120, 120), 75, new Color(96, 165, 250));
_sb.BorderCircle(new Vector2(120, 120), 75, new Color(191, 219, 254), 4f);
_sb.DrawCircle(new Vector2(120, 120), 75, new Color(96, 165, 250), new Color(191, 219, 254), 4f);
```

## Common parameters

* `thickness` is the size of the border in world units. The border grows inward from the shape's edge.
* `rounded` rounds the shape's corners. It is a distance in world units.
* `rotation` is an angle in radians. Shapes rotate around their own center.
* `aaSize` is the size of the anti-aliasing edge in pixels. The default is `1.5f`. Lower it to get a sharper edge, raise it to get a softer one.

Positions and sizes are in world units.

## Circle

A circle is defined by a center and a radius.

```csharp
_sb.FillCircle(new Vector2(120, 120), 75, Color.White);
```

![A circle](circle.png)

## Ellipse

An ellipse is defined by a center, a horizontal radius, and a vertical radius.

```csharp
_sb.FillEllipse(new Vector2(120, 120), 100, 50, Color.White);
```

![An ellipse](ellipse.png)

## Line

A line is defined by two points and a radius. The radius is half the line's thickness. The end caps are rounded. A line with the same start and end positions is drawn as a circle.

```csharp
_sb.FillLine(new Vector2(100, 20), new Vector2(450, 80), 20, Color.White);
```

![A line](line.png)

## Rectangle

A rectangle is defined by its top left corner and a size.

```csharp
_sb.FillRectangle(new Vector2(100, 100), new Vector2(200, 100), Color.White);
```

![A rectangle](rectangle.png)

The corners can be rounded. Pass a single number to round every corner by the same amount:

```csharp
_sb.FillRectangle(new Vector2(100, 100), new Vector2(200, 100), Color.White, 10f);
```

![A rectangle with rounded corners](rectangle-rounded.png)

Or pass a `CornerRadii` to control each corner. The order is top left, top right, bottom right, bottom left:

```csharp
_sb.FillRectangle(new Vector2(100, 100), new Vector2(200, 100), Color.White, new CornerRadii(10f, 20f, 30f, 40f));
```

![A rectangle with a different radius on each corner](rectangle-corner-radii.png)

`CornerRadii` also has shorter constructors. With two numbers, the first one is used for the top left and bottom right corners, the second one for the top right and bottom left corners. The radii are clamped so that they never exceed half of the rectangle's smaller side.

## Hexagon

A hexagon is defined by a center and a radius. The top and bottom edges are flat. The radius is the distance from the center to the flat edges.

```csharp
_sb.FillHexagon(new Vector2(120, 120), 75, Color.White);
```

![A hexagon](hexagon.png)

## Equilateral triangle

An equilateral triangle is defined by a center and a radius. The radius is the radius of the circle that fits inside the triangle. The triangle points down. Use the rotation to orient it in any direction.

```csharp
_sb.FillEquilateralTriangle(new Vector2(120, 120), 50, Color.White, rotation: MathF.PI);
```

![An equilateral triangle pointing up](equilateral-triangle.png)

## Triangle

A triangle is defined by three points. The points can be given in any order.

```csharp
_sb.FillTriangle(new Vector2(100, 100), new Vector2(200, 100), new Vector2(150, 200), Color.White);
```

![A triangle](triangle.png)

## Arc

An arc is a stroke that follows a circle. It is defined by a center, two angles, the radius of the circle, and the half thickness of the stroke. The angles are in radians. An angle of 0 points to the right and angles increase clockwise. The end caps are rounded.

```csharp
_sb.FillArc(new Vector2(120, 120), 0f, MathF.PI, 75, 10, Color.White);
```

![An arc with rounded end caps](arc.png)

## Ring

A ring is the same as an arc except that the end caps are flat.

```csharp
_sb.FillRing(new Vector2(120, 120), 0f, MathF.PI, 75, 10, Color.White);
```

![A ring with flat end caps](ring.png)

## Follow up

Anywhere a shape takes a `Color`, it can take a gradient instead. Read the [Gradients](../gradients/README.md) guide to learn how.
