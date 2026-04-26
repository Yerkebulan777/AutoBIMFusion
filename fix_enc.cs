using System;
using System.IO;
using System.Text;
class Program {
    static void Main() {
        string path = @"AutoBIMFusion\Application\Merge\Layouts\ViewportLayoutExporter.cs";
        string text = File.ReadAllText(path, Encoding.UTF8);

        // Character replace map based on the specific Cyrillic substitution seen
        // If UTF-8 bytes were interpreted as Windows-1251, 
        // byte 0xD0 translates to Windows-1251 'Ð' 
        // byte 0xD1 translates to Windows-1251 'Ñ' (as in 'Ñ', 'Ñ')
        
        // Wait, 'ý' is UTF-8 D1 8D.
        // D1 in CP1251 = 'Ñ'  (U+0421)
        // 8D in CP1251 = ''  (U+040C) -> "Ñ"
        // 'ê' is UTF-8 D0 BA.
        // D0 in CP1251 = 'Ð'  (U+0420)
        // BA in CP1251 = 'º'  (U+0454) Ukrainian ie?
        // Wait, in CP1251 0xBA is 'ê'? NO. 0xBA is Ukrainian 'º'.
        // BUT the text shows "ÑÐºÑÐ¿Ð¾Ñ€Ñ‚". 
        // 'º' in visually similar fonts might look like 'º' -> 'º' is 0xBA in 1251! But the text uses 'º' which is U+0454.
        
        // Let's decode bytes using 1251.
        byte[] bytes = new byte[text.Length];
        
        for (int i = 0; i < text.Length; i++) {
            char c = text[i];
            
            // Map character back to its CP1251 byte value!
            byte b = 0x3F; // Default '?'
            
            if (c <= 127) {
                b = (byte)c;
            } else {
                // Brute-force the CP1251 byte for this char
                for (int v = 128; v <= 255; v++) {
                    string s = Encoding.GetEncoding(1251).GetString(new byte[] { (byte)v });
                    if (s[0] == c) {
                        b = (byte)v;
                        break;
                    }
                }
            }
            bytes[i] = b;
        }

        string result = Encoding.UTF8.GetString(bytes);
        Console.WriteLine(result.Substring(300, 200));

        File.WriteAllText(path, result, new UTF8Encoding(false));
    }
}
