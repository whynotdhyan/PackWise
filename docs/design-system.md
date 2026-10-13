# Northstar Labs design system

`src/Northstar.Design` is the shared UI foundation for every Northstar Labs desktop app.
PackWise is the first product built on it. A new app gets the same look, controls, icons, dialogs
and brand system by referencing one project and choosing its own letters and accent colour.

## Start a new app

1. Reference the library:

   ```xml
   <ProjectReference Include="..\Northstar.Design\Northstar.Design.csproj" />
   ```

2. Merge the theme in `App.xaml`:

   ```xml
   <ResourceDictionary.MergedDictionaries>
     <ResourceDictionary Source="pack://application:,,,/Northstar.Design;component/Themes/Palette.Light.xaml" />
     <ResourceDictionary Source="pack://application:,,,/Northstar.Design;component/Themes/Controls.xaml" />
   </ResourceDictionary.MergedDictionaries>
   ```

3. Start the theme with your brand accent in `OnStartup`:

   ```csharp
   ThemeManager.Initialize(AppTheme.System, new BrandAccent(
       Light: Color.FromRgb(0x5B, 0x4B, 0xD6),   // your accent on light backgrounds
       Dark:  Color.FromRgb(0x8E, 0x82, 0xF2)));  // and on dark backgrounds
   ```

   Hover, pressed, tint and text shades are derived automatically.

4. Use one XAML namespace for everything:

   ```xml
   xmlns:ns="https://northstarlabs.dev/design"
   ```

   Windows derive from `ns:ThemedWindow` (set `IsDialog="True"` for dialogs). That gives you the
   palette, dark title bars that follow the theme, and Escape-to-close for dialogs.

## Brand marks

Every product icon uses the same `MonogramLogo` geometry: a dark rounded tile, a thin inset
outline and a bold two-letter monogram, with an optional signature mark.

| Product | Letters | Colours | Mark |
|---|---|---|---|
| Northstar Labs | `Nı` | Navy tile, cream letters | Teal north star replaces the dot of the i |
| PackWise | `Pw` | Deep teal tile, bright teal letters | A small "packed box" in the bowl of the P |

To add a product, declare a new preset next to the existing ones:

```csharp
public static MonogramLogo MyApp { get; } = new(
    "Ma",
    Color.FromRgb(0x14, 0x10, 0x2E),       // tile
    Color.FromRgb(0x0E, 0x0B, 0x22),       // tile edge (subtle gradient)
    Color.FromRgb(0x9B, 0x8C, 0xFF),       // outline
    Color.FromRgb(0x9B, 0x8C, 0xFF),       // letters
    [new MonogramDecoration(MonogramMark.Star, 1, 0.5, -0.36, 0.20, Colors.White)]);
```

Show it in XAML with `<ns:BrandTile Logo="..." />`, and generate the `.ico` and 512 px PNG with
`dotnet run --project eng/IconTool`.

## What's included

| Area | Contents |
|---|---|
| `Themes/Palette.*.xaml` | Colour tokens (`Color.*`, `Brush.*`) for light and dark: surfaces, text, borders, accent, status colours |
| `Themes/Controls.xaml` | Buttons (default, `Button.Accent`, `Button.Danger`, `Button.Ghost`, `Button.Icon`, `Button.Toolbar`, `Button.Link`), text boxes with placeholders, check boxes, radio buttons and option cards, combo boxes, menus, context menus, slim scroll bars, list views with grid columns, tabs, progress bars, tooltips, cards and banners, text styles (`Text.Title`, `Text.Subtitle`, `Text.Label`, `Text.Secondary`, `Text.Caption`, `Text.SectionHeader`) |
| `Theming/ThemeManager` | Live light/dark switching, follow-Windows mode, brand accents, themed title bars |
| `Controls/ThemedWindow` | Window base class |
| `Icons/` | `GlyphIcon` and the shared line-icon set (`IconKind`): 24×24 grid, 1.6 stroke, round caps, two-tone accent fill |
| `Dialogs/MessageDialog` | Themed replacement for `MessageBox` with info, success, warning, error and question styles |
| `Branding/` | `MonogramLogo` presets and the `BrandTile` control |

## Principles

- **Familiar, then better.** Keep the layout people already know; improve clarity and safety.
- **Calm surfaces.** Neutral slate surfaces with one accent; colour signals state, not decoration.
- **Plain language.** Say what happened and what the user can do. No jargon or blame.
- **Never colour alone.** Status always pairs colour with an icon or text.
- **Accessible by default.** Visible focus rings, keyboard access everywhere, screen-reader names on
  icon buttons, high-DPI rendering.
