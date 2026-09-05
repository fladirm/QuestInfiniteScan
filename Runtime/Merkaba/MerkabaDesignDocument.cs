using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Genesis.RoomScan
{
    public enum MerkabaDesignTool
    {
        Brush,
        SurfaceBrush,
        SpatialBrush,
        Spray,
        Line
    }

    public enum MerkabaBrushShape
    {
        Round,
        Square
    }

    [Serializable]
    public sealed class MerkabaDesignSample
    {
        public Vector3 position;
        public Vector3 normal;
        public bool hasNormal;
        public float radius;
    }

    [Serializable]
    public sealed class MerkabaDesignStroke
    {
        public int id;
        public MerkabaDesignTool tool;
        public Color color = Color.white;
        public float opacity = 1f;
        public float flow = 1f;
        public float hardness = 0.8f;
        public float saturation = 1f;
        public float radius = 0.01f;
        public MerkabaBrushShape shape = MerkabaBrushShape.Round;
        public uint seed;
        public List<MerkabaDesignSample> samples = new();

        internal MerkabaDesignStroke CopyWithIdAndSamples(int replacementId,
            List<MerkabaDesignSample> replacementSamples) => new()
        {
            id = replacementId,
            tool = tool,
            color = color,
            opacity = opacity,
            flow = flow,
            hardness = hardness,
            saturation = saturation,
            radius = radius,
            shape = shape,
            seed = seed,
            samples = replacementSamples ?? new List<MerkabaDesignSample>()
        };
    }

    /// <summary>
    /// Session-local design authority. Positions are stored in RoomSpaceRoot
    /// coordinates; the document never becomes canonical M8 state.
    /// </summary>
    [Serializable]
    public sealed class MerkabaDesignDocument
    {
        internal const string Format = "QuestMerkabaDesign";
        internal const int CurrentVersion = 1;

        public string format = Format;
        public int version = CurrentVersion;
        public int nextStrokeId = 1;
        public List<MerkabaDesignStroke> strokes = new();

        internal int AllocateStrokeId() => nextStrokeId++;

        internal static MerkabaDesignDocument Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new MerkabaDesignDocument();
            string json = File.ReadAllText(path, Encoding.UTF8);
            MerkabaDesignDocument document = JsonUtility.FromJson<
                MerkabaDesignDocument>(json);
            if (document == null || document.format != Format ||
                document.version != CurrentVersion)
                throw new InvalidDataException(
                    "Session design document has an unsupported format.");
            document.strokes ??= new List<MerkabaDesignStroke>();
            int greatestId = 0;
            foreach (MerkabaDesignStroke stroke in document.strokes)
            {
                if (stroke == null) continue;
                stroke.samples ??= new List<MerkabaDesignSample>();
                greatestId = Math.Max(greatestId, stroke.id);
            }
            document.nextStrokeId = Math.Max(document.nextStrokeId,
                greatestId + 1);
            return document;
        }

        internal void Save(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException(
                    "An active scan session is required to save design work.");
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException(
                    "Design document path has no session directory.");
            Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(
                JsonUtility.ToJson(this, true) + "\n");
            using (var stream = new FileStream(temporary, FileMode.Create,
                       FileAccess.Write, FileShare.None, 64 * 1024,
                       FileOptions.SequentialScan))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            MerkabaFilePublishing.Publish(temporary, path);
        }
    }

    internal readonly struct MerkabaPaintSettings
    {
        internal readonly Color Color;
        internal readonly float Opacity;
        internal readonly float Flow;
        internal readonly float Hardness;
        internal readonly float Saturation;
        internal readonly float Radius;
        internal readonly MerkabaBrushShape Shape;

        internal MerkabaPaintSettings(Color color, float opacity, float flow,
            float hardness, float saturation, float radius,
            MerkabaBrushShape shape)
        {
            Color = color;
            Opacity = Mathf.Clamp01(opacity);
            Flow = Mathf.Clamp01(flow);
            Hardness = Mathf.Clamp01(hardness);
            Saturation = Mathf.Clamp01(saturation);
            Radius = Mathf.Clamp(radius, 0.001f, 0.25f);
            Shape = shape;
        }
    }
}
