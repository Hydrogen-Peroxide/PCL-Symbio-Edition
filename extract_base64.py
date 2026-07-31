import json, base64
with open(r'C:\Users\XouYa\AppData\Local\Temp\trae\toolcall-output\6fa0206b-bef9-48dc-acaf-f604e3b45ff0.txt', 'r') as f:
    data = json.load(f)
decoded = base64.b64decode(data['content']).decode('utf-8')
with open(r'C:\Users\XouYa\OneDrive\Desktop\PCL-Symbio Edition\Plain Craft Launcher 2\FormMain.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(decoded)
print(f'Written {len(decoded)} chars')