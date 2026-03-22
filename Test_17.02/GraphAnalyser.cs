using System;
using System.Collections.Generic;
using System.Linq;

namespace GraphEditor
{
    public static class GraphAnalyzer
    {
        /// <summary>
        /// Рассчитывает коэффициент гипердетализации на основе структурной эквивалентности.
        public static double CalculateKhd(IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges)
        {
            var nodeList = nodes.ToList();
            int v = nodeList.Count;

            if (v < 3) return 0.0;

            var adjacencyList = BuildAdjacencyList(nodeList, edges);

            int[] parent = FindEquivalenceClasses(nodeList, adjacencyList);

            int uniqueRoles = CountUniqueTopologicalRoles(nodeList, adjacencyList, parent);

            return CalculateFinalMetric(uniqueRoles, v);
        }

        /// <summary>
        ///  Построение словаря смежности (кто с кем связан)
        /// </summary>
        private static Dictionary<GraphNode, HashSet<GraphNode>> BuildAdjacencyList(List<GraphNode> nodes, IEnumerable<GraphEdge> edges)
        {
            var adj = new Dictionary<GraphNode, HashSet<GraphNode>>();
            foreach (var n in nodes)
            {
                adj[n] = new HashSet<GraphNode>();
            }

            foreach (var e in edges)
            {
                if (e.NodeA != null && e.NodeB != null && e.NodeA != e.NodeB)
                {
                    adj[e.NodeA].Add(e.NodeB);
                    adj[e.NodeB].Add(e.NodeA);
                }
            }
            return adj;
        }

        /// <summary>
        /// Группировка структурно эквивалентных узлов (Union-Find)
        /// </summary>
        private static int[] FindEquivalenceClasses(List<GraphNode> nodes, Dictionary<GraphNode, HashSet<GraphNode>> adj)
        {
            int v = nodes.Count;
            int[] parent = new int[v];
            for (int i = 0; i < v; i++) parent[i] = i;

            int Find(int i) => parent[i] == i ? i : (parent[i] = Find(parent[i]));
            void Union(int i, int j)
            {
                int rootI = Find(i);
                int rootJ = Find(j);
                if (rootI != rootJ) parent[rootI] = rootJ;
            }

            for (int i = 0; i < v; i++)
            {
                for (int j = i + 1; j < v; j++)
                {
                    var n1Neighbors = adj[nodes[i]];
                    var n2Neighbors = adj[nodes[j]];

                    double jaccardEx = CalculateJaccardIndex(n1Neighbors, n2Neighbors);

                    var n1Inclusive = new HashSet<GraphNode>(n1Neighbors) { nodes[i] };
                    var n2Inclusive = new HashSet<GraphNode>(n2Neighbors) { nodes[j] };
                    double jaccardIn = CalculateJaccardIndex(n1Inclusive, n2Inclusive);

                    if (jaccardEx >= 0.8 || jaccardIn >= 0.8)
                    {
                        Union(i, j);
                    }
                }
            }
            return parent;
        }

        /// <summary>
        ///  Расчет коэффициента Жаккара
        /// </summary>
        private static double CalculateJaccardIndex(HashSet<GraphNode> setA, HashSet<GraphNode> setB)
        {
            int intersection = setA.Intersect(setB).Count();
            int union = setA.Union(setB).Count();
            return union == 0 ? 0 : (double)intersection / union;
        }

        /// <summary>
        /// Подсчет количества уникальных ролей с учетом транзитных цепей
        /// </summary>
        private static int CountUniqueTopologicalRoles(List<GraphNode> nodes, Dictionary<GraphNode, HashSet<GraphNode>> adj, int[] parent)
        {
            int Find(int i)
            {
                int current = i;
                while (parent[current] != current) current = parent[current];
                return current;
            }

            var classes = new Dictionary<int, List<GraphNode>>();
            for (int i = 0; i < nodes.Count; i++)
            {
                int root = Find(i);
                if (!classes.ContainsKey(root)) classes[root] = new List<GraphNode>();
                classes[root].Add(nodes[i]);
            }

            int uniqueRoles = 0;
            foreach (var cls in classes.Values)
            {
                if (cls.Count == 1)
                {
                    if (adj[cls[0]].Count != 2)
                    {
                        uniqueRoles++;
                    }
                }
                else
                {
                    uniqueRoles++;
                }
            }
            return uniqueRoles;
        }

        /// <summary>
        /// Шаг 4: Нормализация итогового коэффициента
        /// </summary>
        private static double CalculateFinalMetric(int uniqueRoles, int totalNodes)
        {
            double khd = 1.0 - ((double)uniqueRoles / totalNodes);

            // Жесткие границы от 0.0 до 1.0
            if (khd < 0) khd = 0;
            if (khd > 1) khd = 1;

            return Math.Round(khd, 3);
        }

        public static List<List<GraphNode>> GetNodesToGroup(IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges)
        {
            var nodeList = nodes.ToList();
            var resultGroups = new List<List<GraphNode>>();
            if (nodeList.Count < 3) return resultGroups;

            var adjacencyList = BuildAdjacencyList(nodeList, edges);
            var (parent, _) = FindEquivalenceClassesWithReasons(nodeList, adjacencyList);
            var classes = GroupNodesByEquivalenceClass(nodeList, parent);

            foreach (var kvp in classes)
            {
                if (kvp.Value.Count > 1)
                {
                    resultGroups.Add(kvp.Value);
                }
            }
            return resultGroups;
        }

        /// <summary>
        /// Выдает рекомендации по группировке на основе структурной эквивалентности (Жаккард)
        /// </summary>
        public static List<string> GetGroupingRecommendations(IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges)
        {
            var nodeList = nodes.ToList();
            if (nodeList.Count < 3) return new List<string>();
            var adjacencyList = BuildAdjacencyList(nodeList, edges);

            var (parent, groupingReasons) = FindEquivalenceClassesWithReasons(nodeList, adjacencyList);

            var classes = GroupNodesByEquivalenceClass(nodeList, parent);

            return FormatRecommendations(classes, groupingReasons);
        }

        /// <summary>
        /// Поиск классов эквивалентности с сохранением причины объединения
        /// </summary>
        private static (int[] parent, Dictionary<int, string> reasons) FindEquivalenceClassesWithReasons(
            List<GraphNode> nodes,
            Dictionary<GraphNode, HashSet<GraphNode>> adj)
        {
            int v = nodes.Count;
            int[] parent = new int[v];
            for (int i = 0; i < v; i++) parent[i] = i;

            var reasons = new Dictionary<int, string>();

            int Find(int i) => parent[i] == i ? i : (parent[i] = Find(parent[i]));
            void Union(int i, int j, string reason)
            {
                int rootI = Find(i);
                int rootJ = Find(j);
                if (rootI != rootJ)
                {
                    parent[rootI] = rootJ;
                    reasons[rootJ] = reason; 
                }
            }

            for (int i = 0; i < v; i++)
            {
                for (int j = i + 1; j < v; j++)
                {
                    var n1Neighbors = adj[nodes[i]];
                    var n2Neighbors = adj[nodes[j]];

                    double jaccardEx = CalculateJaccardIndex(n1Neighbors, n2Neighbors);

                    var n1Inclusive = new HashSet<GraphNode>(n1Neighbors) { nodes[i] };
                    var n2Inclusive = new HashSet<GraphNode>(n2Neighbors) { nodes[j] };
                    double jaccardIn = CalculateJaccardIndex(n1Inclusive, n2Inclusive);

                    if (jaccardEx >= 0.8)
                    {
                        Union(i, j, "Параллельные дубликаты (имеют общих соседей)");
                    }
                    else if (jaccardIn >= 0.8)
                    {
                        Union(i, j, "Сверхплотный клубок (сильная внутренняя связность)");
                    }
                }
            }

            return (parent, reasons);
        }

        /// <summary>
        /// Группировка объектов GraphNode по их корню
        /// </summary>
        private static Dictionary<int, List<GraphNode>> GroupNodesByEquivalenceClass(List<GraphNode> nodes, int[] parent)
        {
            var classes = new Dictionary<int, List<GraphNode>>();

            for (int i = 0; i < nodes.Count; i++)
            {
                int current = i;
                while (parent[current] != current) current = parent[current];
                int root = current;

                if (!classes.ContainsKey(root)) classes[root] = new List<GraphNode>();
                classes[root].Add(nodes[i]);
            }

            return classes;
        }

        /// <summary>
        /// Форматирование итогового текста для UI
        /// </summary>
        private static List<string> FormatRecommendations(
            Dictionary<int, List<GraphNode>> classes,
            Dictionary<int, string> groupingReasons)
        {
            var recommendations = new List<string>();

            foreach (var kvp in classes)
            {
                if (kvp.Value.Count > 1)
                {
                    string reason = groupingReasons.ContainsKey(kvp.Key)
                        ? groupingReasons[kvp.Key]
                        : "Структурная избыточность";

                    var sortedNames = kvp.Value
                        .Select(n => n.Name)
                        .OrderBy(n => n.Length)
                        .ThenBy(n => n)
                        .ToList();

                    string nodeNames = string.Join(", ", sortedNames);
                    recommendations.Add($"[{nodeNames}]\nПричина: {reason}\n");
                }
            }

            return recommendations;
        }
    }
}