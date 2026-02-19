using GraphEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Shapes;

namespace GraphEditor
{
    public class GraphEdge
    {
        public GraphNode NodeA { get; set; }
        public GraphNode NodeB { get; set; }
        public Line VisualLine { get; set; }
    }
}
