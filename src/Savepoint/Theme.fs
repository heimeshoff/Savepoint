namespace Savepoint

open Avalonia.Media

/// Color palette and styling constants matching the dark theme design
module Theme =

    // Primary colors
    let background = Color.Parse("#13171f")
    let surface = Color.Parse("#1d222c")
    let surfaceLight = Color.Parse("#282e39")
    let primary = Color.Parse("#25abfe")
    let primaryHover = Color.Parse("#0e8bd9")

    // Accent colors
    let secondary = Color.Parse("#ff8b00")      // Orange
    let accentGreen = Color.Parse("#93c01f")    // Green - Fresh/Success
    let accentYellow = Color.Parse("#fae92b")   // Yellow - Warning/Stale
    let accentPink = Color.Parse("#eb68a0")     // Pink - Accent
    let accentRed = Color.Parse("#e74c3c")      // Red - Critical/Error

    // Text colors
    let textPrimary = Color.Parse("#ffffff")
    let textSecondary = Color.Parse("#94a3b8")  // Slate-400
    let textMuted = Color.Parse("#64748b")      // Slate-500

    // Border colors
    let border = Color.Parse("#ffffff0d")       // White 5%
    let borderHover = Color.Parse("#25abfe80")  // Primary 50%

    // Brushes for convenience
    module Brushes =
        let background = SolidColorBrush(background)
        let surface = SolidColorBrush(surface)
        let surfaceLight = SolidColorBrush(surfaceLight)
        let primary = SolidColorBrush(primary)
        let primaryHover = SolidColorBrush(primaryHover)
        let secondary = SolidColorBrush(secondary)
        let accentGreen = SolidColorBrush(accentGreen)
        let accentYellow = SolidColorBrush(accentYellow)
        let accentPink = SolidColorBrush(accentPink)
        let accentRed = SolidColorBrush(accentRed)
        let textPrimary = SolidColorBrush(textPrimary)
        let textSecondary = SolidColorBrush(textSecondary)
        let textMuted = SolidColorBrush(textMuted)
        let border = SolidColorBrush(border)
        let transparent = SolidColorBrush(Colors.Transparent)

    // Sizing constants
    module Sizes =
        let sidebarWidth = 256.0
        let headerHeight = 64.0
        let cardRadius = 16.0
        let buttonRadius = 8.0
        let spacing = 16.0
        let spacingSmall = 8.0
        let spacingLarge = 24.0

    // Typography
    module Typography =
        let fontSizeXs = 11.0
        let fontSizeSm = 13.0
        let fontSizeMd = 14.0
        let fontSizeLg = 18.0
        let fontSizeXl = 24.0
        let fontSizeXxl = 30.0
