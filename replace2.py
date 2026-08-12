import os
import glob
from replace import replacements

path = "/Users/liling/Documents/Code/Netcore/CdycDataAcquisitionPlaform/DAP.Presentation.AvaloniaApp/Views/*.axaml"
files = glob.glob(path)

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
