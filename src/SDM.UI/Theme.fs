namespace SDM.UI.Theme

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Media

/// Centralized design tokens for the SDM UI — ports the old XDM WPF/GTK
/// color schemes (LightTheme.xaml / DarkTheme.xaml) into F# immutable values.
module Colors =
    //
    // Dark theme (default — matches XDM DarkTheme.xaml)
    //
    let dark =
        [ ("CategoryListBg", "#1B1C1E")
          ("CategoryHighlight", "#114C7B")
          ("CategoryNormal", "#B5BCC9")
          ("CategoryHover", "#252628")
          ("ToolButton", "#B5BCC9")
          ("ToolbarBorder", "#000000")
          ("MainBg", "#1B1C1E")
          ("StatusBarBg", "#232428")
          ("StatusBarIcon", "#777D88")
          ("ListBg", "#232428")
          ("ListText", "#B5BCC9")
          ("ListSelectedBg", "#114C7B")
          ("ListSelectedText", "#FFFFFF")
          ("Text", "#ABB2BF")
          ("TextInputBg", "#2B2B2B")
          ("ProgressFill", "#114C7B")
          ("ProgressBg", "#343537")
          ("SearchBg", "#232428")
          ("SearchPlaceholder", "#969696")
          ("ButtonBg", "#2B2B2B")
          ("ButtonDisabled", "#404040")
          ("Hyperlink", "#25608F")
          ("Border", "#3E4042")
          ("Danger", "#E06C75")
          ("Success", "#98C379")
          ("Warning", "#E5C07B") ]

    //
    // Light theme — matches XDM LightTheme.xaml
    //
    let light =
        [ ("CategoryListBg", "#24292E")
          ("CategoryHighlight", "#0A6AB6")
          ("CategoryNormal", "#808080")
          ("CategoryHover", "#2E3338")
          ("ToolButton", "#828282")
          ("ToolbarBorder", "#E5E5E5")
          ("MainBg", "#F3F3F3")
          ("StatusBarBg", "#FFFFFF")
          ("StatusBarIcon", "#1E90FF")
          ("ListBg", "#FFFFFF")
          ("ListText", "#696969")
          ("ListSelectedBg", "#E6E6E6")
          ("ListSelectedText", "#000000")
          ("Text", "#333333")
          ("TextInputBg", "#FFFFFF")
          ("ProgressFill", "#1E90FF")
          ("ProgressBg", "#DCDCDC")
          ("SearchBg", "#FFFFFF")
          ("SearchPlaceholder", "#969696")
          ("ButtonBg", "#FFFFFF")
          ("ButtonDisabled", "#CCCCCC")
          ("Hyperlink", "#1E90FF")
          ("Border", "#E5E5E5")
          ("Danger", "#E81123")
          ("Success", "#107C10")
          ("Warning", "#FF8C00") ]

    // ── Resolved brushes (dark theme by default) ──
    let categoryListBg = Color.Parse "#1B1C1E"
    let categoryHighlight = Color.Parse "#114C7B"
    let categoryNormal = Color.Parse "#B5BCC9"
    let categoryHover = Color.Parse "#252628"
    let toolButtonFg = Color.Parse "#B5BCC9"
    let toolbarBorder = Color.Parse "#000000"
    let mainBg = Color.Parse "#1B1C1E"
    let statusBarBg = Color.Parse "#232428"
    let statusBarIcon = Color.Parse "#777D88"
    let listBg = Color.Parse "#232428"
    let listText = Color.Parse "#B5BCC9"
    let listSelectedBg = Color.Parse "#114C7B"
    let listSelectedText = Color.Parse "#FFFFFF"
    let textFg = Color.Parse "#ABB2BF"
    let textInputBg = Color.Parse "#2B2B2B"
    let progressFill = Color.Parse "#114C7B"
    let progressBg = Color.Parse "#343537"
    let searchBg = Color.Parse "#232428"
    let searchPlaceholder = Color.Parse "#969696"
    let buttonBg = Color.Parse "#2B2B2B"
    let hyperlink = Color.Parse "#25608F"
    let border = Color.Parse "#3E4042"
    let danger = Color.Parse "#E06C75"
    let success = Color.Parse "#98C379"
    let warning = Color.Parse "#E5C07B"

    // ── AB Download Manager–inspired additions ──
    /// Toolbar background — slightly elevated from main bg, matching AB's toolbar strip
    let toolbarBg = Color.Parse "#222426"
    /// Alternating even-row background for the download list
    let rowEvenBg = Color.Parse "#1E2022"
    /// Alternating odd-row background for the download list
    let rowOddBg = Color.Parse "#252729"
    /// Column header background — distinctly darker than the list rows
    let colHeaderBg = Color.Parse "#191B1D"
    /// Primary "New Download" button accent (AB's purple-ish gradient)
    let accentPrimary = Color.Parse "#5C6BC0"
    let accentPrimaryHover = Color.Parse "#7986CB"
    /// Icon-only toolbar button hover highlight
    let toolBtnHover = Color.Parse "#2E3035"
    /// Subtle separator between toolbar icon buttons
    let toolSeparator = Color.Parse "#333538"
    /// Column splitter / divider color
    let colDivider = Color.Parse "#2C2E31"

    // ── Brushes ──
    let categoryListBgBrush = SolidColorBrush categoryListBg
    let categoryHighlightBrush = SolidColorBrush categoryHighlight
    let categoryNormalBrush = SolidColorBrush categoryNormal
    let categoryHoverBrush = SolidColorBrush categoryHover
    let toolButtonFgBrush = SolidColorBrush toolButtonFg
    let toolbarBorderBrush = SolidColorBrush toolbarBorder
    let mainBgBrush = SolidColorBrush mainBg
    let statusBarBgBrush = SolidColorBrush statusBarBg
    let statusBarIconBrush = SolidColorBrush statusBarIcon
    let listBgBrush = SolidColorBrush listBg
    let listTextBrush = SolidColorBrush listText
    let listSelectedBgBrush = SolidColorBrush listSelectedBg
    let listSelectedTextBrush = SolidColorBrush listSelectedText
    let textFgBrush = SolidColorBrush textFg
    let textInputBgBrush = SolidColorBrush textInputBg
    let progressFillBrush = SolidColorBrush progressFill
    let progressBgBrush = SolidColorBrush progressBg
    let searchBgBrush = SolidColorBrush searchBg
    let searchPlaceholderBrush = SolidColorBrush searchPlaceholder
    let buttonBgBrush = SolidColorBrush buttonBg
    let hyperlinkBrush = SolidColorBrush hyperlink
    let borderBrush = SolidColorBrush border
    let dangerBrush = SolidColorBrush danger
    let successBrush = SolidColorBrush success
    let warningBrush = SolidColorBrush warning

    // ── AB-inspired brushes ──
    let toolbarBgBrush = SolidColorBrush toolbarBg
    let rowEvenBgBrush = SolidColorBrush rowEvenBg
    let rowOddBgBrush = SolidColorBrush rowOddBg
    let colHeaderBgBrush = SolidColorBrush colHeaderBg
    let accentPrimaryBrush = SolidColorBrush accentPrimary
    let accentPrimaryHoverBrush = SolidColorBrush accentPrimaryHover
    let toolBtnHoverBrush = SolidColorBrush toolBtnHover
    let toolSeparatorBrush = SolidColorBrush toolSeparator
    let colDividerBrush = SolidColorBrush colDivider

    // File-type category icon tints (from old XDM color-ri-* resources)
    let documentIcon = Color.Parse "#5AC8FA" // light blue
    let musicIcon = Color.Parse "#34C759" // green
    let videoIcon = Color.Parse "#007AFF" // blue
    let archiveIcon = Color.Parse "#FF9500" // orange
    let programIcon = Color.Parse "#FF3A31" // red
    let otherIcon = Color.Parse "#6AC4DC" // cyan

    let documentIconBrush = SolidColorBrush documentIcon
    let musicIconBrush = SolidColorBrush musicIcon
    let videoIconBrush = SolidColorBrush videoIcon
    let archiveIconBrush = SolidColorBrush archiveIcon
    let programIconBrush = SolidColorBrush programIcon
    let otherIconBrush = SolidColorBrush otherIcon

/// File-type icon colour lookup (matches old XDM FileExtensionToColorConverter)
[<RequireQualifiedAccess>]
module Icon =
    let forCategory (cat: string) : IBrush =
        match cat with
        | "document" -> Colors.documentIconBrush :> IBrush
        | "music" -> Colors.musicIconBrush :> IBrush
        | "video" -> Colors.videoIconBrush :> IBrush
        | "archive" -> Colors.archiveIconBrush :> IBrush
        | "program" -> Colors.programIconBrush :> IBrush
        | _ -> Colors.otherIconBrush :> IBrush

/// Spacing constants aligned to old XDM padding patterns
module Spacing =
    let xs = Thickness 4.0
    let sm = Thickness 8.0
    let md = Thickness 12.0
    let lg = Thickness 16.0
    let xl = Thickness 24.0
    // Toolbar with slightly more vertical room to fit icon+label buttons
    let toolbar = Thickness(8.0, 6.0, 8.0, 6.0)
    let sidebarItem = Thickness(10.0, 5.0, 10.0, 5.0)
    let sidebarSubItem = Thickness(25.0, 5.0, 10.0, 5.0)
    let statusBar = Thickness(8.0, 4.0, 8.0, 4.0)
    // Tighter row padding so the list feels dense
    let row = Thickness(0.0, 5.0, 8.0, 5.0)
    // Header cell padding for column headers
    let colHeader = Thickness(6.0, 6.0, 6.0, 6.0)

/// Typography presets matching old XDM
module Typography =
    let sidebarItem = FontWeight.Normal, 13.0
    let sidebarHeader = FontWeight.SemiBold, 13.0
    let toolbarButton = FontWeight.Normal, 12.0
    let listItem = FontWeight.Normal, 12.0
    let listItemBold = FontWeight.SemiBold, 12.0
    let statusText = FontWeight.Normal, 11.0
    let caption = FontWeight.Normal, 10.0
    let title = FontWeight.Bold, 14.0

/// Category definitions matching XDM Category.cs defaults
[<RequireQualifiedAccess>]
module CategoryDefs =
    open SDM.Domain

    [<Struct>]
    type CategoryItem =
        { Name: string
          DisplayName: string
          IsTopLevel: bool
          IsPredefined: bool }

    /// Sidebar categories in display order
    let all: CategoryItem list =
        [ { Name = "ALL_UNFINISHED"
            DisplayName = "All Unfinished"
            IsTopLevel = true
            IsPredefined = true }
          { Name = "ALL_FINISHED"
            DisplayName = "All Finished"
            IsTopLevel = true
            IsPredefined = true }
          { Name = "CAT_DOCUMENTS"
            DisplayName = "Document"
            IsTopLevel = false
            IsPredefined = true }
          { Name = "CAT_MUSIC"
            DisplayName = "Music"
            IsTopLevel = false
            IsPredefined = true }
          { Name = "CAT_VIDEOS"
            DisplayName = "Video"
            IsTopLevel = false
            IsPredefined = true }
          { Name = "CAT_COMPRESSED"
            DisplayName = "Compressed"
            IsTopLevel = false
            IsPredefined = true }
          { Name = "CAT_PROGRAMS"
            DisplayName = "Application"
            IsTopLevel = false
            IsPredefined = true } ]

    let displayNameOf (name: string) : string =
        all
        |> List.tryFind (fun c -> c.Name = name)
        |> Option.map (fun c -> c.DisplayName)
        |> Option.defaultValue name

    let categoryOf (entry: SDM.Domain.DownloadEntry) : string =
        match entry.Status with
        | Completed _ -> "ALL_FINISHED"
        | Error _ -> "ALL_FINISHED"
        | _ -> "ALL_UNFINISHED"
