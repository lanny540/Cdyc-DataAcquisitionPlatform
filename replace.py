import os
import glob

replacements = {
    "AppBackgroundBrush": "SemiColorBackground0",
    "SidebarBackgroundBrush": "SemiColorBackground1",
    "RailBackgroundBrush": "SemiColorBackground0",
    "RailIconBrush": "SemiColorText2",
    "NavItemBorderBrush": "SemiColorPrimary",
    "NavItemActiveBrush": "SemiColorBackground3",
    "SurfaceMutedBrush": "SemiColorBackground1",
    "SurfaceBorderBrush": "SemiColorBorder",
    "ShellSurfaceBrush": "SemiColorBackground0",
    "AccentSoftBrush": "SemiColorPrimaryLight",
    "AccentBrush": "SemiColorPrimary",
    "PrimaryTextBrush": "SemiColorText0",
    "SecondaryTextBrush": "SemiColorText1",
    "MutedTextBrush": "SemiColorText2",
    "FocusTaskAccentBrush": "SemiColorWarning"
}

path = "/Users/liling/Documents/Code/Netcore/CdycDataAcquisitionPlaform/DAP.Presentation.AvaloniaApp/Views/**/*.axaml"
files = glob.glob(path, recursive=True)

for file in files:
    with open(file, 'r', encoding='utf-8') as f:
        content = f.read()
    
    original = content
    for old, new in replacements.items():
        content = content.replace(old, new)
        
    if content != original:
        with open(file, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"Updated {file}")