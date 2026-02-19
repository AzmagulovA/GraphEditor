using System.Windows;
using Microsoft.Win32; // Для OpenFileDialog

namespace GraphEditor
{
    public partial class NodeInfoWindow : Window
    {
        private GraphNode _node;

        public NodeInfoWindow(GraphNode node)
        {
            InitializeComponent();
            _node = node;
            this.DataContext = _node; // Привязываем окно к объекту данных
        }

        private void BtnAddParam_Click(object sender, RoutedEventArgs e)
        {
            _node.Parameters.Add(new NodeParameter { Name = "Новый параметр" });
        }

        private void BtnDeleteParam_Click(object sender, RoutedEventArgs e)
        {
            // Получаем объект из строки, где была нажата кнопка
            if ((sender as FrameworkElement).DataContext is NodeParameter param)
            {
                _node.Parameters.Remove(param);
            }
        }

        private void BtnLoadXsl_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "XSL Files (*.xsl)|*.xsl|All Files (*.*)|*.*" };
            if (dlg.ShowDialog() == true)
            {
                if ((sender as FrameworkElement).DataContext is NodeParameter param)
                {
                    param.XslFilePath = dlg.FileName;
                    MessageBox.Show($"Файл привязан: {dlg.FileName}", "Успех");
                }
            }
        }
    }
}