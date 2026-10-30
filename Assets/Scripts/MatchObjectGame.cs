using TMPro;
using UnityEngine;

public class MatchObjectGame : MonoBehaviour
{
    [System.Serializable]
    public class Obj
    {
        public Transform root;
        [HideInInspector] public int shape, color, size;
        [HideInInspector] public Quaternion rot = Quaternion.identity;
    }

    public Obj player, target;
    public TMP_Text feedback;

    static readonly Color[] Colors = { Color.red, Color.green, Color.blue };
    static readonly float[] Sizes = { 0.1f, 0.2f, 0.3f };
    static readonly Vector3[] Axes = { Vector3.right, Vector3.up, Vector3.forward };

    void Start() { NewTarget(); ResetObject(); }
    void Update() { Draw(player); Draw(target); }

    void NewTarget()
    {
        target.shape = Random.Range(0, 3);
        target.color = Random.Range(0, 3);
        target.size = Random.Range(0, 3);
        target.rot = Quaternion.Euler(Random.Range(0, 4) * 90, Random.Range(0, 4) * 90, Random.Range(0, 4) * 90);
    }

    public void SetShape(int i) => player.shape = i;
    public void SetColor(int i) => player.color = i;
    public void SetSize(int i) => player.size = i;
    public void Rotate(int axis) => player.rot = Quaternion.AngleAxis(90, Axes[axis]) * player.rot;

    public void ResetObject()
    {
        player.shape = 0; player.color = 0; player.size = 1; player.rot = Quaternion.identity;
        Say("Haz que tu objeto sea igual al objetivo", Color.white);
    }

    public void Confirm()
    {
        bool ok = player.shape == target.shape && player.color == target.color && player.size == target.size && SameRotation();
        Say(ok ? "¡Correcto!" : "Algo no coincide. Sigue intentando", ok ? Color.green : new Color(1f, 0.6f, 0f));
    }

    bool SameRotation()
    {
        if (player.shape != 2) return true;
        return Mathf.Abs(Vector3.Dot(player.rot * Vector3.up, target.rot * Vector3.up)) > 0.9f;
    }

    void Draw(Obj o)
    {
        for (int i = 0; i < 3; i++)
        {
            var s = o.root.GetChild(i);
            s.gameObject.SetActive(i == o.shape);
            if (i == o.shape) s.GetComponent<Renderer>().material.color = Colors[o.color];
        }
        o.root.localScale = Vector3.one * Sizes[o.size];
        o.root.localRotation = o.rot;
    }

    void Say(string text, Color c) { feedback.text = text; feedback.color = c; }
}