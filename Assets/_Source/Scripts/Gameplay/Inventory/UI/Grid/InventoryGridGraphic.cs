using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class InventoryGridGraphic : MaskableGraphic
    {
        private readonly List<InventoryGridQuad> _quads = new();

        public int QuadCount => _quads.Count;

        public void ClearQuads()
        {
            _quads.Clear();
            SetVerticesDirty();
        }

        public void AddQuad(Rect topLeftRect, Color quadColor)
        {
            if (topLeftRect.width <= 0f || topLeftRect.height <= 0f || quadColor.a <= 0f)
            {
                return;
            }

            _quads.Add(new InventoryGridQuad(topLeftRect, quadColor));
        }

        public void Commit() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = rectTransform.rect;
            UIVertex vertex = UIVertex.simpleVert;

            for (int i = 0; i < _quads.Count; i++)
            {
                InventoryGridQuad quad = _quads[i];
                float xMin = rect.xMin + quad.Rect.xMin;
                float xMax = rect.xMin + quad.Rect.xMax;
                float yMax = rect.yMax - quad.Rect.yMin;
                float yMin = rect.yMax - quad.Rect.yMax;
                Color32 quadColor = quad.Color * color;
                int index = vertexHelper.currentVertCount;
                vertex.color = quadColor;
                vertex.position = new Vector3(xMin, yMin);
                vertexHelper.AddVert(vertex);
                vertex.position = new Vector3(xMin, yMax);
                vertexHelper.AddVert(vertex);
                vertex.position = new Vector3(xMax, yMax);
                vertexHelper.AddVert(vertex);
                vertex.position = new Vector3(xMax, yMin);
                vertexHelper.AddVert(vertex);
                vertexHelper.AddTriangle(index, index + 1, index + 2);
                vertexHelper.AddTriangle(index + 2, index + 3, index);
            }
        }
    }
}
