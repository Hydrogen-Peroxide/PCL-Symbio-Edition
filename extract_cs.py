import json
import base64

# Read the JSON file
with open(r'C:\Users\XouYa\AppData\Local\Temp\trae\toolcall-output\6fa0206b-bef9-48dc-acaf-f604e3b45ff0.txt', 'r', encoding='utf-8') as f:
    data = json.load(f)

# Decode the base64 content
base64_content = data['content']
decoded = base64.b64decode(base64_content).decode('utf-8')

# Write to the target file
with open(r'C:\Users\XouYa\OneDrive\Desktop\PCL-Symbio Edition\Plain Craft Launcher 2\FormMain.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(decoded)

print(f'Successfully written {len(decoded)} characters to FormMain.xaml.cs')