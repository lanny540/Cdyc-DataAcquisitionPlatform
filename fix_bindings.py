import glob

path = "/Users/liling/Documents/Code/Netcore/CdycDataAcquisitionPlaform/DAP.Presentation.AvaloniaApp/Views/**/*.axaml"
files = glob.glob(path, recursive=True)

for file in files:
    with open(file, 'r', encoding='utf-8') as f:
        content = f.read()
    
    original = content
    content = content.replace("{Binding SemiColorPrimary}", "{Binding AccentBrush}")
    content = content.replace("{Binding FocusTaskSemiColorWarning}", "{Binding FocusTaskAccentBrush}")
    content = content.replace("{Binding FocusTaskSemiColorPrimary}", "{Binding FocusTaskAccentBrush}")
    
    if content != original:
        with open(file, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"Fixed {file}")
