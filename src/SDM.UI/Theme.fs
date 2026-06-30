namespace SDM.UI.Theme

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Media

/// Centralized design tokens for the SDM dark-themed UI.
/// All colors, spacing, and typography values are defined here
/// to ensure visual consistency across all programmatic views.
module Colors =
    let background = Color.Parse "#0f172a"
    let surface = Color.Parse "#1e293b"
    let surfaceBorder = Color.Parse "#334155"
    let primary = Color.Parse "#3b82f6"
    let primaryHover = Color.Parse "#2563eb"
    let success = Color.Parse "#22c55e"
    let danger = Color.Parse "#ef4444"
    let warning = Color.Parse "#f59e0b"
    let textPrimary = Color.Parse "#f8fafc"
    let textSecondary = Color.Parse "#94a3b8"
    let textMuted = Color.Parse "#64748b"
    let accent = Color.Parse "#8b5cf6"

    let backgroundBrush = SolidColorBrush background
    let surfaceBrush = SolidColorBrush surface
    let surfaceBorderBrush = SolidColorBrush surfaceBorder
    let primaryBrush = SolidColorBrush primary
    let primaryHoverBrush = SolidColorBrush primaryHover
    let successBrush = SolidColorBrush success
    let dangerBrush = SolidColorBrush danger
    let warningBrush = SolidColorBrush warning
    let textPrimaryBrush = SolidColorBrush textPrimary
    let textSecondaryBrush = SolidColorBrush textSecondary
    let textMutedBrush = SolidColorBrush textMuted
    let accentBrush = SolidColorBrush accent

/// Spacing constants aligned to a 4px grid
module Spacing =
    let xs = Thickness 4.0
    let sm = Thickness 8.0
    let md = Thickness 12.0
    let lg = Thickness 16.0
    let xl = Thickness 24.0

    let smHorizontal = Thickness(8.0, 0.0)
    let mdHorizontal = Thickness(12.0, 0.0)
    let lgVertical = Thickness(0.0, 16.0)

/// Typography presets
module Typography =
    let title = FontWeight.SemiBold, 14.0
    let body = FontWeight.Normal, 12.0
    let caption = FontWeight.Normal, 11.0
    let heading = FontWeight.Bold, 16.0

/// Reusable styling builders for programmatic controls
[<AutoOpen>]
module StyleHelpers =
    let primaryButton (content: string) =
        Button(Content = content, Padding = Thickness(12.0, 6.0))

    let captionText (text: string) =
        TextBlock(Text = text, FontSize = 11.0, Opacity = 0.6)

    let verticalSeparator =
        Rectangle(Width = 1.0, Fill = Colors.surfaceBorderBrush, Margin = Thickness(4.0, 4.0))
