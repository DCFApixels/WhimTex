using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed class GraphEdge : Edge
        {
            public GraphEdge() { }

            protected override EdgeControl CreateEdgeControl() =>
                new GraphEdgeControl(this) { capRadius = 4, interceptWidth = 6 };

            public override void OnPortChanged(bool isInput)
            {
                ((GraphEdgeControl)edgeControl).RefreshPorts();
                base.OnPortChanged(isInput);
            }
        }

        private sealed class GraphEdgeControl : EdgeControl
        {
            // Native EdgeControl uses a 12px lead and 16px turn diameter.
            const float Lead = 12, Turn = 16;
            readonly GraphEdge edge;

            internal GraphEdgeControl(GraphEdge edge) => this.edge = edge;

            internal void RefreshPorts() => PointsChanged();

            protected override void ComputeControlPoints()
            {
                base.ComputeControlPoints();
                if (controlPoints == null) return;
                float reach = Mathf.Clamp(Vector2.Distance(from, to) * 2, Turn, Turn + Lead);
                float outputReach = ShortenedReach(reach, edge.output);
                float inputReach = ShortenedReach(reach, edge.input);
                controlPoints[1] = from + (outputOrientation == Orientation.Horizontal ? Vector2.right : Vector2.up) * outputReach;
                controlPoints[2] = to - (inputOrientation == Orientation.Horizontal ? Vector2.right : Vector2.up) * inputReach;
            }

            static float ShortenedReach(float reach, Port port) =>
                Mathf.Min(reach, Mathf.Max(Turn, reach - Outset(port)) * 1.15f);

            static float Outset(Port port) => port?.node switch
            {
                ParameterView => 20,
                NodeView => 12,
                _ => 0
            };
        }
    }
}
