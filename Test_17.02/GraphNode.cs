using System;
using System.Collections.ObjectModel;

namespace GraphEditor
{
    public class GraphNode : ObservableObject
    {
        public string Id { get; } = "ND-" + Guid.NewGuid().ToString().Substring(0, 4).ToUpper();

        private string _name = "Node";
        public string Name
        {
            get => _name;
            set 
            {   _name = value; 
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayInfo)); 
            }
        }

        private double _importance = 1.0;
        public double Importance
        {
            get => _importance;
            set 
            { 
                _importance = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(DisplayInfo)); 
            }
        }

        public ObservableCollection<NodeParameter> Parameters { get; set; } = new ObservableCollection<NodeParameter>();

        // Локальное состояние отображения для конкретного узла
        private bool _showName = true;

        // Свойство, к которому вяжется TextBlock в XAML
        public string DisplayInfo => _showName ? Name : Importance.ToString("F2");

        // Метод для переключения режима извне
        public void SetDisplayMode(bool showName)
        {
            _showName = showName;
            // Уведомляем интерфейс, что свойство DisplayInfo "как бы" изменилось
            OnPropertyChanged(nameof(DisplayInfo));
        }
    }
}