using System;
using UnityEngine;

namespace Genesis.RoomScan.HeavyCompute
{
    public sealed class DiffSoupPackedMlp
    {
        public Matrix4x4[] W1 { get; internal set; }
        public Vector4[] B1 { get; internal set; }
        public Matrix4x4[] W2 { get; internal set; }
        public Vector4[] B2 { get; internal set; }
        public Matrix4x4[] W3 { get; internal set; }
        public Vector4 B3 { get; internal set; }
    }

    /// <summary>
    /// Frozen CPU counterpart of the pinned upstream viewer shader. It provides validation,
    /// LUT addressing, uniform packing, and a scalar reference evaluator used to prove that
    /// Unity's shader consumes row-major PyTorch weights and SH2 features in the same order.
    /// </summary>
    public static class DiffSoupShaderContract
    {
        public const string FeatureEncoding = "diffsoup-sh2-mlp16-v1";
        public const int MaximumRenderedFaces = 1_000_000;
        public const long MaximumVertexBufferBytes = 128L * 1024 * 1024;
        private const int RenderVertexStride = 28; // position + barycentric/face feature

        public static int LevelSize(int level)
        {
            if (level < 0 || level > 8)
                throw new ArgumentOutOfRangeException(nameof(level));
            if (level == 0) return 3;
            int a = (1 << (level - 1)) + 1;
            int b = (1 << level) + 1;
            return checked(a * b);
        }

        public static bool TryValidateRendererData(DiffSoupArtifactData data,
            out string error)
        {
            error = null;
            DiffSoupModelDescription model = data?.Manifest?.model;
            if (model == null || data.Positions == null || data.Indices == null ||
                data.Mlp == null || data.Lut0Png == null || data.Lut1Png == null)
            {
                error = "DiffSoup renderer payload is incomplete.";
                return false;
            }
            if (!string.Equals(model.featureEncoding, FeatureEncoding,
                    StringComparison.Ordinal))
            {
                error = "DiffSoup appearance network is unsupported.";
                return false;
            }
            if (model.numFaces < 1 || model.numFaces > MaximumRenderedFaces ||
                model.numVertices != data.Positions.Length ||
                data.Indices.Length != checked(model.numFaces * 3))
            {
                error = "DiffSoup renderer mesh counts exceed the mobile contract.";
                return false;
            }
            if ((long)data.Indices.Length * RenderVertexStride > MaximumVertexBufferBytes)
            {
                error = "DiffSoup expanded vertex buffer would exceed 128 MiB.";
                return false;
            }
            try
            {
                long requiredTexels = checked((long)model.numFaces * LevelSize(model.level));
                long availableTexels = checked((long)model.lutWidth * model.lutHeight);
                if (requiredTexels > availableTexels ||
                    availableTexels - requiredTexels >= model.lutWidth)
                {
                    error = "DiffSoup LUT capacity is inconsistent with face/level counts.";
                    return false;
                }
            }
            catch (Exception exception) when (exception is OverflowException ||
                                              exception is ArgumentOutOfRangeException)
            {
                error = "DiffSoup LUT dimensions are invalid: " + exception.Message;
                return false;
            }
            for (int i = 0; i < data.Indices.Length; i++)
                if (data.Indices[i] < 0 || data.Indices[i] >= data.Positions.Length)
                {
                    error = "DiffSoup renderer mesh contains an invalid index.";
                    return false;
                }
            return TryPackMlp(data.Mlp, out _, out error);
        }

        public static bool TryLutAddress(int level, int face, Vector3 barycentric,
            int textureWidth, int textureHeight, out Vector3Int sampleIndices,
            out Vector3 weights, out string error)
        {
            sampleIndices = default;
            weights = default;
            error = null;
            if (face < 0 || textureWidth < 1 || textureHeight < 1 ||
                !IsFinite(barycentric) || barycentric.x < -0.001f ||
                barycentric.y < -0.001f || barycentric.z < -0.001f ||
                Mathf.Abs(barycentric.x + barycentric.y + barycentric.z - 1f) > 0.01f)
            {
                error = "DiffSoup LUT address inputs are invalid.";
                return false;
            }
            try
            {
                int samplesPerFace = LevelSize(level);
                int resolution = 1 << level;
                float b0 = barycentric.x * resolution;
                float b1 = barycentric.y * resolution;
                int x = Mathf.Clamp(Mathf.FloorToInt(b0), 0, resolution - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(b1), 0, resolution - 1 - x);
                b0 -= x;
                b1 -= y;
                bool flip = b0 + b1 > 1f;
                int flipInteger = flip ? 1 : 0;
                int x0 = x + 1;
                int y0 = y;
                int x1 = x;
                int y1 = y + 1;
                int x2 = x + flipInteger;
                int y2 = Math.Min(y + flipInteger, resolution - x2);
                int i0 = TriangularIndex(x0, y0);
                int i1 = TriangularIndex(x1, y1);
                int i2 = TriangularIndex(x2, y2);
                float w0 = flip ? 1f - b1 : b0;
                float w1 = flip ? 1f - b0 : b1;
                int baseIndex = checked(face * samplesPerFace);
                sampleIndices = new Vector3Int(checked(baseIndex + i0),
                    checked(baseIndex + i1), checked(baseIndex + i2));
                weights = new Vector3(w0, w1, 1f - w0 - w1);
                int maximum = checked(textureWidth * textureHeight);
                if (sampleIndices.x >= maximum || sampleIndices.y >= maximum ||
                    sampleIndices.z >= maximum)
                {
                    error = "DiffSoup LUT address exceeds its texture.";
                    return false;
                }
                return true;
            }
            catch (Exception exception) when (exception is OverflowException ||
                                              exception is ArgumentOutOfRangeException)
            {
                error = "DiffSoup LUT address overflowed: " + exception.Message;
                return false;
            }
        }

        public static bool TryPackMlp(DiffSoupMlpWeights source,
            out DiffSoupPackedMlp packed, out string error)
        {
            packed = null;
            error = null;
            if (!ValidateArray(source?.W1, 256) || !ValidateArray(source?.b1, 16) ||
                !ValidateArray(source?.W2, 256) || !ValidateArray(source?.b2, 16) ||
                !ValidateArray(source?.W3, 48) || !ValidateArray(source?.b3, 3))
            {
                error = "DiffSoup MLP weights have an unsupported shape or value.";
                return false;
            }
            packed = new DiffSoupPackedMlp
            {
                W1 = PackDense16(source.W1),
                B1 = PackBias16(source.b1),
                W2 = PackDense16(source.W2),
                B2 = PackBias16(source.b2),
                W3 = PackDense3(source.W3),
                B3 = new Vector4(source.b3[0], source.b3[1], source.b3[2], 0f)
            };
            return true;
        }

        public static bool TryEvaluate(DiffSoupMlpWeights mlp, Vector4 featureA,
            Vector3 featureB, Vector3 viewDirectionChunk, out Vector3 encodedColor,
            out string error)
        {
            encodedColor = default;
            error = null;
            if (!TryPackMlp(mlp, out _, out error) || !IsFinite(featureA) ||
                !IsFinite(featureB) || !IsFinite(viewDirectionChunk) ||
                viewDirectionChunk.sqrMagnitude < 1e-12f)
            {
                error ??= "DiffSoup shader reference inputs are invalid.";
                return false;
            }
            Vector3 direction = viewDirectionChunk.normalized;
            float[] sh = EvaluateSh2(direction);
            var input = new float[16]
            {
                featureA.x, featureA.y, featureA.z, featureA.w,
                featureB.x, featureB.y, featureB.z, sh[0],
                sh[1], sh[2], sh[3], sh[4],
                sh[5], sh[6], sh[7], sh[8]
            };
            float[] first = Dense(mlp.W1, mlp.b1, input, 16, true);
            float[] second = Dense(mlp.W2, mlp.b2, first, 16, true);
            float[] output = Dense(mlp.W3, mlp.b3, second, 3, false);
            for (int i = 0; i < output.Length; i++) output[i] = Sigmoid(output[i]);
            float residual = featureA.w;
            encodedColor = new Vector3(
                Mathf.LerpUnclamped(featureA.x, output[0], residual),
                Mathf.LerpUnclamped(featureA.y, output[1], residual),
                Mathf.LerpUnclamped(featureA.z, output[2], residual));
            return IsFinite(encodedColor);
        }

        private static Matrix4x4[] PackDense16(float[] rowMajor)
        {
            var result = new Matrix4x4[16];
            for (int tileRow = 0; tileRow < 4; tileRow++)
            for (int tileColumn = 0; tileColumn < 4; tileColumn++)
            {
                var matrix = Matrix4x4.zero;
                for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    matrix[row, column] = rowMajor[(tileRow * 4 + row) * 16 +
                                                   tileColumn * 4 + column];
                result[tileRow * 4 + tileColumn] = matrix;
            }
            return result;
        }

        private static Matrix4x4[] PackDense3(float[] rowMajor)
        {
            var result = new Matrix4x4[4];
            for (int tileColumn = 0; tileColumn < 4; tileColumn++)
            {
                var matrix = Matrix4x4.zero;
                for (int row = 0; row < 3; row++)
                for (int column = 0; column < 4; column++)
                    matrix[row, column] = rowMajor[row * 16 + tileColumn * 4 + column];
                result[tileColumn] = matrix;
            }
            return result;
        }

        private static Vector4[] PackBias16(float[] values)
        {
            var result = new Vector4[4];
            for (int i = 0; i < values.Length; i++) result[i / 4][i % 4] = values[i];
            return result;
        }

        private static float[] Dense(float[] weights, float[] bias, float[] input,
            int outputCount, bool relu)
        {
            var output = new float[outputCount];
            for (int row = 0; row < outputCount; row++)
            {
                double value = bias[row];
                for (int column = 0; column < input.Length; column++)
                    value += weights[row * input.Length + column] * input[column];
                output[row] = relu ? Mathf.Max(0f, (float)value) : (float)value;
            }
            return output;
        }

        private static float[] EvaluateSh2(Vector3 direction)
        {
            const float c0 = 0.28209479177387814f;
            const float c1 = 0.4886025119029199f;
            const float c20 = 1.0925484305920792f;
            const float c21 = -1.0925484305920792f;
            const float c22 = 0.31539156525252005f;
            const float c23 = -1.0925484305920792f;
            const float c24 = 0.5462742152960396f;
            float x = direction.x;
            float y = direction.y;
            float z = direction.z;
            float xx = x * x;
            float yy = y * y;
            float zz = z * z;
            return new[]
            {
                c0, -c1 * y, c1 * z, -c1 * x,
                c20 * x * y, c21 * y * z, c22 * (2f * zz - xx - yy),
                c23 * x * z, c24 * (xx - yy)
            };
        }

        private static int TriangularIndex(int x, int y) =>
            checked((x + y) * (x + y + 1) / 2 + y);

        private static float Sigmoid(float value)
        {
            value = Mathf.Clamp(value, -30f, 30f);
            return 1f / (1f + Mathf.Exp(-value));
        }

        private static bool ValidateArray(float[] values, int count)
        {
            if (values == null || values.Length != count) return false;
            for (int i = 0; i < values.Length; i++)
                if (!IsFinite(values[i]) || Mathf.Abs(values[i]) > 1_000_000f)
                    return false;
            return true;
        }

        private static bool IsFinite(Vector4 value) => IsFinite(value.x) &&
            IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        private static bool IsFinite(Vector3 value) => IsFinite(value.x) &&
            IsFinite(value.y) && IsFinite(value.z);
        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
