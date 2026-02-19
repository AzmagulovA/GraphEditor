using GraphEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GraphEditor
{
    public class NodeParameter : ObservableObject
    {
        public string Id { get; } = Guid.NewGuid().ToString().Substring(0, 6).ToUpper(); // Неизменяемый ID
        public string Name { get; set; } = "Параметр";
        public double Importance { get; set; } = 1.0;
        public string XslFilePath { get; set; } // Путь к файлу
    }
}
