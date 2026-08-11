with open('MiniApp/wwwroot/index.html', 'r', encoding='utf-8') as f:
    text = f.read()

try:
    bytes_data = text.encode('cp1251')
    fixed_text = bytes_data.decode('utf-8')
    with open('MiniApp/wwwroot/index.html', 'w', encoding='utf-8') as f:
        f.write(fixed_text)
    print("SUCCESS")
except Exception as e:
    print("ERROR:", e)
