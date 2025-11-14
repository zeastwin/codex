using System;
using System.Collections.Generic;
using System.Linq;

namespace EW_Assistant.Component.MindMap
{
    /// <summary>
    /// 简单的树状布局器，将 MindMapNode 映射为层级坐标，供画布渲染。
    /// </summary>
    public static class MindMapLayoutEngine
    {
        public static (IReadOnlyList<MindMapVisualNode> Nodes, IReadOnlyList<MindMapEdge> Edges) Layout(
            MindMapNode root,
            double horizontalGap = 340,
            double verticalGap = 160,
            double marginX = 180,
            double marginY = 140)
        {
            if (root == null) return (Array.Empty<MindMapVisualNode>(), Array.Empty<MindMapEdge>());

            var nodes = new List<MindMapVisualNode>();
            var edges = new List<MindMapEdge>();
            double cursorY = marginY;

            LayoutInternal(root, 0, ref cursorY, nodes, edges, null, horizontalGap, verticalGap, marginX);

            // 归一化 Y，避免出现负值。
            var minY = nodes.Count > 0 ? nodes.Min(n => n.Y) : marginY;
            if (minY < marginY)
            {
                var offset = marginY - minY;
                foreach (var node in nodes)
                    node.Y += offset;
            }

            var normalizedNodes = nodes.ToList();
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < normalizedNodes.Count; i++)
                {
                    for (int j = i + 1; j < normalizedNodes.Count; j++)
                    {
                        var a = normalizedNodes[i];
                        var b = normalizedNodes[j];
                        if (Math.Abs(a.Depth - b.Depth) > 0) continue;
                        if (a.Bounds.IntersectsWith(b.Bounds))
                        {
                            if (a.Y <= b.Y)
                                b.Y = a.Y + a.Height + verticalGap * 0.6;
                            else
                                a.Y = b.Y + b.Height + verticalGap * 0.6;
                            changed = true;
                        }
                    }
                }
            } while (changed);

            return (normalizedNodes, edges);
        }

        private static double LayoutInternal(
            MindMapNode node,
            int depth,
            ref double cursorY,
            List<MindMapVisualNode> nodes,
            List<MindMapEdge> edges,
            MindMapVisualNode parent,
            double hGap,
            double vGap,
            double marginX)
        {
            var visual = new MindMapVisualNode(node, depth)
            {
                X = marginX + depth * hGap
            };

            nodes.Add(visual);

            var visibleChildren = node.IsExpanded
                ? node.Children?.ToList() ?? new List<MindMapNode>()
                : new List<MindMapNode>();

            double y;
            if (visibleChildren.Count == 0)
            {
                y = cursorY;
                cursorY += vGap;
            }
            else
            {
                double firstChildY = double.MaxValue;
                double lastChildY = double.MinValue;

                foreach (var child in visibleChildren)
                {
                    var childY = LayoutInternal(child, depth + 1, ref cursorY, nodes, edges, visual, hGap, vGap, marginX);
                    firstChildY = Math.Min(firstChildY, childY);
                    lastChildY = Math.Max(lastChildY, childY);
                }

                y = (firstChildY + lastChildY) / 2.0;
            }

            visual.Y = y;

            if (parent != null)
                edges.Add(new MindMapEdge(parent, visual));

            return y;
        }
    }
}
