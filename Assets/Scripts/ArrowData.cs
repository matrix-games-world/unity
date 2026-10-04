using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ArrowData
{
    [Header("Path - Tail to Head")]
    public List<Vector2Int> path = new List<Vector2Int>();

    [Header("Head")]
    public ArrowPathController.Direction headDirection =
        ArrowPathController.Direction.Right;

    [Header("Appearance")]
    public Sprite headSprite;
    public Color arrowColor = Color.black;
}
