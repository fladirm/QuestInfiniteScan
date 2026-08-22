using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.Prism;
using UnityEngine;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Detached measured ContactFilm meshlet vertices used only for bounded overlap
    /// registration. This is derived data; it is not a canonical persistence format.
    /// </summary>
    public sealed class ContactMeshSnapshot
    {
        public int VertexCount { get; set; }
        public byte[] VertexBytes { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Immutable chunk-local point/normal observations safe to hand to a background
    /// registration worker after GPU readback has completed.
    /// </summary>
    public sealed class OverlapPointCloud
    {
        private const int MaximumRawSamples = 2_000_000;
        private readonly Vector3[] _points;
        private readonly Vector3[] _normals;

        private OverlapPointCloud(Vector3[] points, Vector3[] normals)
        {
            _points = points;
            _normals = normals;
        }

        public int Count => _points.Length;
        internal Vector3 PointAt(int index) => _points[index];
        internal Vector3 NormalAt(int index) => _normals[index];

        public static bool TryCreate(IReadOnlyList<Vector3> points,
            IReadOnlyList<Vector3> normals, out OverlapPointCloud cloud,
            out string error)
        {
            cloud = null;
            error = null;
            if (points == null || normals == null || points.Count == 0 ||
                points.Count != normals.Count || points.Count > MaximumRawSamples)
            {
                error = "An overlap cloud requires matching non-empty point/normal arrays " +
                        $"with at most {MaximumRawSamples} samples.";
                return false;
            }

            var pointCopy = new Vector3[points.Count];
            var normalCopy = new Vector3[normals.Count];
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 point = points[i];
                Vector3 normal = normals[i];
                float normalLength = normal.magnitude;
                if (!Finite(point) || !Finite(normal) || normalLength < 1e-6f)
                {
                    error = $"Overlap sample {i} contains a non-finite point or invalid normal.";
                    return false;
                }
                pointCopy[i] = point;
                normalCopy[i] = normal / normalLength;
            }
            cloud = new OverlapPointCloud(pointCopy, normalCopy);
            return true;
        }

        private static bool Finite(Vector3 value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z);
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Converts a detached ContactFilm meshlet readback into a small deterministic ICP
    /// input. Appearance, topology and canonical posterior state never enter the worker.
    /// </summary>
    public static class OverlapPointCloudBuilder
    {
        public static bool TryCreate(ContactMeshSnapshot snapshot,
            int maximumSamples, out OverlapPointCloud cloud, out string error)
        {
            cloud = null;
            error = null;
            if (snapshot == null || snapshot.VertexCount <= 0 ||
                maximumSamples < 16 || maximumSamples > 1_000_000 ||
                snapshot.VertexBytes == null || snapshot.VertexBytes.Length !=
                (long)snapshot.VertexCount * ContactMeshletVertexGpu.Stride)
            {
                error = "Live-mesh snapshot cannot be sampled for overlap registration.";
                return false;
            }

            int candidateCount = Math.Min(snapshot.VertexCount,
                checked(maximumSamples * 4));
            var candidatePoints = new List<Vector3>(candidateCount);
            var candidateNormals = new List<Vector3>(candidateCount);
            for (int i = 0; i < candidateCount; i++)
            {
                int vertexIndex = (int)((long)i * snapshot.VertexCount /
                                        candidateCount);
                int offset = vertexIndex * ContactMeshletVertexGpu.Stride;
                var point = new Vector3(
                    BitConverter.ToSingle(snapshot.VertexBytes, offset),
                    BitConverter.ToSingle(snapshot.VertexBytes, offset + 4),
                    BitConverter.ToSingle(snapshot.VertexBytes, offset + 8));
                var normal = ContactMeshletVertexGpu.UnpackNormal(
                    BitConverter.ToUInt32(snapshot.VertexBytes, offset + 16));
                if (!Finite(point) || !Finite(normal) || normal.sqrMagnitude < 1e-12f)
                    continue;
                candidatePoints.Add(point);
                candidateNormals.Add(normal);
            }
            if (candidatePoints.Count < 6)
            {
                error = "Live mesh contains too few finite point/normal samples.";
                return false;
            }

            int finalCount = Math.Min(maximumSamples, candidatePoints.Count);
            var points = new Vector3[finalCount];
            var normals = new Vector3[finalCount];
            for (int i = 0; i < finalCount; i++)
            {
                int candidateIndex = (int)((long)i * candidatePoints.Count /
                                           finalCount);
                points[i] = candidatePoints[candidateIndex];
                normals[i] = candidateNormals[candidateIndex];
            }
            return OverlapPointCloud.TryCreate(points, normals, out cloud, out error);
        }

        private static bool Finite(Vector3 value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z);
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Fully detached input for one target-to-source overlap registration. The pose
    /// convention matches <see cref="PoseGraphEdgeRecord.sourceFromTarget"/>.
    /// </summary>
    public sealed class OverlapRegistrationRequest
    {
        private OverlapRegistrationRequest(string sourceChunkId, int sourceChunkRevision,
            OverlapPointCloud sourceCloud, string targetChunkId, int targetChunkRevision,
            OverlapPointCloud targetCloud, RigidPoseData initialSourceFromTarget,
            long observedUnixMilliseconds)
        {
            SourceChunkId = sourceChunkId;
            SourceChunkRevision = sourceChunkRevision;
            SourceCloud = sourceCloud;
            TargetChunkId = targetChunkId;
            TargetChunkRevision = targetChunkRevision;
            TargetCloud = targetCloud;
            InitialSourceFromTarget = initialSourceFromTarget;
            ObservedUnixMilliseconds = observedUnixMilliseconds;
        }

        public string SourceChunkId { get; }
        public int SourceChunkRevision { get; }
        public OverlapPointCloud SourceCloud { get; }
        public string TargetChunkId { get; }
        public int TargetChunkRevision { get; }
        public OverlapPointCloud TargetCloud { get; }
        public RigidPoseData InitialSourceFromTarget { get; }
        public long ObservedUnixMilliseconds { get; }

        public static bool TryCreate(string sourceChunkId, int sourceChunkRevision,
            OverlapPointCloud sourceCloud, string targetChunkId, int targetChunkRevision,
            OverlapPointCloud targetCloud, RigidPoseData initialSourceFromTarget,
            long observedUnixMilliseconds, out OverlapRegistrationRequest request,
            out string error)
        {
            request = null;
            error = null;
            if (!StoragePath.IsSafeIdentifier(sourceChunkId, 64) ||
                !StoragePath.IsSafeIdentifier(targetChunkId, 64) ||
                string.Equals(sourceChunkId, targetChunkId, StringComparison.Ordinal) ||
                sourceChunkRevision < 0 || targetChunkRevision < 0 ||
                sourceCloud == null || targetCloud == null ||
                observedUnixMilliseconds < 0 || !FinitePose(initialSourceFromTarget))
            {
                error = "Overlap registration request is invalid.";
                return false;
            }
            request = new OverlapRegistrationRequest(sourceChunkId,
                sourceChunkRevision, sourceCloud, targetChunkId, targetChunkRevision,
                targetCloud, initialSourceFromTarget, observedUnixMilliseconds);
            return true;
        }

        private static bool FinitePose(RigidPoseData pose)
        {
            if (!Finite(pose.position.x) || !Finite(pose.position.y) ||
                !Finite(pose.position.z))
                return false;
            float norm = pose.rotation.x * pose.rotation.x +
                         pose.rotation.y * pose.rotation.y +
                         pose.rotation.z * pose.rotation.z +
                         pose.rotation.w * pose.rotation.w;
            return Finite(norm) && Mathf.Abs(norm - 1f) <= 0.01f;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public sealed class OverlapConstraintEstimate
    {
        private OverlapConstraintEstimate(bool succeeded, string failureReason,
            RigidPoseData sourceFromTarget, float confidence,
            float[] covarianceDiagonal, int correspondenceCount,
            float rmsMeters, int iterations, bool converged, string provenance)
        {
            Succeeded = succeeded;
            FailureReason = failureReason ?? string.Empty;
            SourceFromTarget = sourceFromTarget;
            Confidence = confidence;
            CovarianceDiagonal = covarianceDiagonal ?? Array.Empty<float>();
            CorrespondenceCount = correspondenceCount;
            RmsMeters = rmsMeters;
            Iterations = iterations;
            Converged = converged;
            Provenance = provenance ?? string.Empty;
        }

        public bool Succeeded { get; }
        public string FailureReason { get; }
        public RigidPoseData SourceFromTarget { get; }
        public float Confidence { get; }
        public float[] CovarianceDiagonal { get; }
        public int CorrespondenceCount { get; }
        public float RmsMeters { get; }
        public int Iterations { get; }
        public bool Converged { get; }
        public string Provenance { get; }

        internal static OverlapConstraintEstimate Failure(string reason)
        {
            return new OverlapConstraintEstimate(false, reason,
                RigidPoseData.Identity, 0f, Array.Empty<float>(), 0,
                0f, 0, false, string.Empty);
        }

        internal static OverlapConstraintEstimate Success(RigidPoseData pose,
            float confidence, float[] covariance, int correspondences,
            float rmsMeters, int iterations, bool converged, string provenance)
        {
            return new OverlapConstraintEstimate(true, string.Empty, pose,
                confidence, covariance, correspondences, rmsMeters, iterations,
                converged, provenance);
        }
    }

    /// <summary>
    /// Pluggable boundary between realtime chunking and optional registration work.
    /// Implementations must not touch Unity objects or live PRISM GPU state from a worker.
    /// </summary>
    public interface IOverlapConstraintEstimator
    {
        Task<OverlapConstraintEstimate> EstimateAsync(
            OverlapRegistrationRequest request, CancellationToken cancellationToken);
    }

    public sealed class NoneOverlapConstraintEstimator : IOverlapConstraintEstimator
    {
        public Task<OverlapConstraintEstimate> EstimateAsync(
            OverlapRegistrationRequest request, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<OverlapConstraintEstimate>(cancellationToken);
            return Task.FromResult(OverlapConstraintEstimate.Failure(
                "Overlap registration backend is disabled."));
        }
    }

    public sealed class PointToPlaneIcpSettings
    {
        public int MaximumSamples { get; set; } = 4096;
        public int MaximumIterations { get; set; } = 20;
        public int MinimumCorrespondences { get; set; } = 48;
        public float MinimumInlierRatio { get; set; } = 0.15f;
        public float MaximumCorrespondenceDistanceMeters { get; set; } = 0.30f;
        public float MaximumNormalAngleDegrees { get; set; } = 55f;
        public float HuberDistanceMeters { get; set; } = 0.03f;
        public float MaximumAcceptedRmsMeters { get; set; } = 0.08f;
        public float MinimumConfidence { get; set; } = 0.05f;
        public float Damping { get; set; } = 1e-6f;
        public float MaximumTranslationStepMeters { get; set; } = 0.10f;
        public float MaximumRotationStepDegrees { get; set; } = 5f;
        public float TranslationConvergenceMeters { get; set; } = 0.0001f;
        public float RotationConvergenceDegrees { get; set; } = 0.01f;

        public bool TryValidate(out string error)
        {
            error = null;
            if (MaximumSamples < 16 || MaximumSamples > 1_000_000 ||
                MaximumIterations < 1 || MaximumIterations > 200 ||
                MinimumCorrespondences < 6 || MinimumCorrespondences > MaximumSamples ||
                !Range(MinimumInlierRatio, 0.001f, 1f) ||
                !Range(MaximumCorrespondenceDistanceMeters, 0.001f, 10f) ||
                !Range(MaximumNormalAngleDegrees, 0.1f, 89.9f) ||
                !Range(HuberDistanceMeters, 0.00001f,
                    MaximumCorrespondenceDistanceMeters) ||
                !Range(MaximumAcceptedRmsMeters, 0.00001f,
                    MaximumCorrespondenceDistanceMeters) ||
                !Range(MinimumConfidence, 0.0001f, 1f) ||
                !Range(Damping, 1e-12f, 1f) ||
                !Range(MaximumTranslationStepMeters, 0.0001f, 10f) ||
                !Range(MaximumRotationStepDegrees, 0.001f, 90f) ||
                !Range(TranslationConvergenceMeters, 1e-7f, 0.1f) ||
                !Range(RotationConvergenceDegrees, 1e-6f, 5f))
            {
                error = "Point-to-plane ICP settings are invalid.";
                return false;
            }
            return true;
        }

        internal PointToPlaneIcpSettings Copy()
        {
            return (PointToPlaneIcpSettings)MemberwiseClone();
        }

        private static bool Range(float value, float minimum, float maximum)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                   value >= minimum && value <= maximum;
        }
    }

    /// <summary>
    /// Deterministic bounded CPU point-to-plane ICP intended for finalized overlap
    /// meshes. Work runs outside the scan frame, uses no live GPU resources, and has
    /// explicit sample/iteration bounds.
    /// </summary>
    public sealed class PointToPlaneIcpEstimator : IOverlapConstraintEstimator
    {
        private const int Dimension = 6;
        private readonly PointToPlaneIcpSettings _settings;

        public PointToPlaneIcpEstimator(PointToPlaneIcpSettings settings = null)
        {
            settings ??= new PointToPlaneIcpSettings();
            if (!settings.TryValidate(out string error))
                throw new ArgumentException(error, nameof(settings));
            _settings = settings.Copy();
        }

        public Task<OverlapConstraintEstimate> EstimateAsync(
            OverlapRegistrationRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
                return Task.FromResult(OverlapConstraintEstimate.Failure(
                    "Overlap registration request is null."));
            return Task.Run(() => Estimate(request, cancellationToken),
                cancellationToken);
        }

        private OverlapConstraintEstimate Estimate(OverlapRegistrationRequest request,
            CancellationToken cancellationToken)
        {
            Sample[] source = SelectSamples(request.SourceCloud,
                _settings.MaximumSamples);
            Sample[] target = SelectSamples(request.TargetCloud,
                _settings.MaximumSamples);
            if (source.Length < _settings.MinimumCorrespondences ||
                target.Length < _settings.MinimumCorrespondences)
            {
                return OverlapConstraintEstimate.Failure(
                    "Overlap clouds do not contain enough bounded samples.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var spatialIndex = new SourceSpatialIndex(source,
                _settings.MaximumCorrespondenceDistanceMeters);
            RigidPoseData pose = request.InitialSourceFromTarget;
            int iterations = 0;
            bool converged = false;
            NormalEquations equations = null;

            for (int iteration = 0; iteration < _settings.MaximumIterations;
                 iteration++)
            {
                equations = BuildEquations(source, target, spatialIndex, pose,
                    cancellationToken);
                if (!HasEnoughOverlap(equations, target.Length, out string overlapError))
                    return OverlapConstraintEstimate.Failure(overlapError);
                if (!TrySolve(equations.Matrix, equations.Gradient,
                        _settings.Damping, out double[] increment))
                {
                    return OverlapConstraintEstimate.Failure(
                        "Overlap geometry is singular after damped ICP assembly.");
                }

                Vector3 rotationStep = ClampMagnitude(new Vector3(
                    (float)increment[0], (float)increment[1], (float)increment[2]),
                    _settings.MaximumRotationStepDegrees * Mathf.Deg2Rad);
                Vector3 translationStep = ClampMagnitude(new Vector3(
                    (float)increment[3], (float)increment[4], (float)increment[5]),
                    _settings.MaximumTranslationStepMeters);
                var delta = new RigidPoseData(translationStep,
                    QuaternionFromRotationVector(rotationStep));
                pose = delta * pose;
                pose.rotation = Normalize(pose.rotation);
                iterations = iteration + 1;
                if (translationStep.magnitude <=
                        _settings.TranslationConvergenceMeters &&
                    rotationStep.magnitude <=
                        _settings.RotationConvergenceDegrees * Mathf.Deg2Rad)
                {
                    converged = true;
                    break;
                }
            }

            equations = BuildEquations(source, target, spatialIndex, pose,
                cancellationToken);
            if (!HasEnoughOverlap(equations, target.Length, out string finalOverlapError))
                return OverlapConstraintEstimate.Failure(finalOverlapError);
            float rms = Mathf.Sqrt((float)(equations.ResidualSquared /
                                           equations.CorrespondenceCount));
            if (!Finite(rms) || rms > _settings.MaximumAcceptedRmsMeters)
            {
                return OverlapConstraintEstimate.Failure(
                    $"ICP residual {rms.ToString("F6", CultureInfo.InvariantCulture)} m " +
                    "exceeds the accepted bound.");
            }
            if (!TryCovariance(equations.Matrix, _settings.Damping,
                    Math.Max(equations.ResidualSquared /
                             equations.CorrespondenceCount, 1e-10),
                    out float[] covariance))
            {
                return OverlapConstraintEstimate.Failure(
                    "ICP covariance could not be estimated.");
            }

            float inlierRatio = equations.CorrespondenceCount / (float)target.Length;
            float coverage = Mathf.Clamp01(equations.CorrespondenceCount /
                (float)(_settings.MinimumCorrespondences * 2));
            float fit = Mathf.Exp(-rms /
                Mathf.Max(_settings.HuberDistanceMeters, 1e-6f));
            float confidence = Mathf.Clamp01(inlierRatio * coverage * fit);
            if (confidence < _settings.MinimumConfidence)
            {
                return OverlapConstraintEstimate.Failure(
                    "ICP confidence is below the configured acceptance threshold.");
            }

            string provenance = string.Format(CultureInfo.InvariantCulture,
                "point-to-plane-icp/v1;sourceRevision={0};targetRevision={1};" +
                "inliers={2}/{3};iterations={4};rms={5:F6}",
                request.SourceChunkRevision, request.TargetChunkRevision,
                equations.CorrespondenceCount, target.Length, iterations, rms);
            return OverlapConstraintEstimate.Success(pose, confidence, covariance,
                equations.CorrespondenceCount, rms, iterations, converged,
                provenance);
        }

        private bool HasEnoughOverlap(NormalEquations equations, int targetCount,
            out string error)
        {
            error = null;
            if (equations.CorrespondenceCount < _settings.MinimumCorrespondences)
            {
                error = "ICP found too few compatible overlap correspondences.";
                return false;
            }
            if (equations.CorrespondenceCount / (float)targetCount <
                _settings.MinimumInlierRatio)
            {
                error = "ICP overlap ratio is below the configured threshold.";
                return false;
            }
            return true;
        }

        private NormalEquations BuildEquations(Sample[] source, Sample[] target,
            SourceSpatialIndex spatialIndex, RigidPoseData sourceFromTarget,
            CancellationToken cancellationToken)
        {
            var result = new NormalEquations();
            float minimumNormalDot = Mathf.Cos(
                _settings.MaximumNormalAngleDegrees * Mathf.Deg2Rad);
            float maximumDistanceSquared =
                _settings.MaximumCorrespondenceDistanceMeters *
                _settings.MaximumCorrespondenceDistanceMeters;
            var jacobian = new double[Dimension];

            for (int i = 0; i < target.Length; i++)
            {
                if ((i & 255) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                Vector3 point = sourceFromTarget.TransformPoint(target[i].Point);
                Vector3 normal = sourceFromTarget.rotation * target[i].Normal;
                int nearest = spatialIndex.FindNearest(point, source,
                    maximumDistanceSquared);
                if (nearest < 0)
                    continue;
                Sample match = source[nearest];
                if (Vector3.Dot(match.Normal, normal) < minimumNormalDot)
                    continue;

                float residual = Vector3.Dot(match.Normal, point - match.Point);
                float absoluteResidual = Mathf.Abs(residual);
                float robustWeight = absoluteResidual <= _settings.HuberDistanceMeters
                    ? 1f
                    : _settings.HuberDistanceMeters / absoluteResidual;
                Vector3 rotationJacobian = Vector3.Cross(point, match.Normal);
                jacobian[0] = rotationJacobian.x;
                jacobian[1] = rotationJacobian.y;
                jacobian[2] = rotationJacobian.z;
                jacobian[3] = match.Normal.x;
                jacobian[4] = match.Normal.y;
                jacobian[5] = match.Normal.z;
                for (int row = 0; row < Dimension; row++)
                {
                    double weighted = jacobian[row] * robustWeight;
                    result.Gradient[row] += weighted * residual;
                    for (int column = row; column < Dimension; column++)
                        result.Matrix[row, column] += weighted * jacobian[column];
                }
                result.ResidualSquared += residual * residual;
                result.CorrespondenceCount++;
            }
            for (int row = 1; row < Dimension; row++)
            for (int column = 0; column < row; column++)
                result.Matrix[row, column] = result.Matrix[column, row];
            return result;
        }

        private static Sample[] SelectSamples(OverlapPointCloud cloud, int maximum)
        {
            int count = Math.Min(cloud.Count, maximum);
            var samples = new Sample[count];
            for (int i = 0; i < count; i++)
            {
                int sourceIndex = (int)((long)i * cloud.Count / count);
                samples[i] = new Sample(cloud.PointAt(sourceIndex),
                    cloud.NormalAt(sourceIndex));
            }
            return samples;
        }

        private static bool TrySolve(double[,] matrix, double[] gradient,
            float damping, out double[] solution)
        {
            solution = new double[Dimension];
            var augmented = new double[Dimension, Dimension + 1];
            for (int row = 0; row < Dimension; row++)
            {
                double diagonalScale = Math.Max(1.0, Math.Abs(matrix[row, row]));
                for (int column = 0; column < Dimension; column++)
                    augmented[row, column] = matrix[row, column];
                augmented[row, row] += damping * diagonalScale;
                augmented[row, Dimension] = -gradient[row];
            }

            for (int pivot = 0; pivot < Dimension; pivot++)
            {
                int bestRow = pivot;
                double bestMagnitude = Math.Abs(augmented[pivot, pivot]);
                for (int row = pivot + 1; row < Dimension; row++)
                {
                    double magnitude = Math.Abs(augmented[row, pivot]);
                    if (magnitude > bestMagnitude)
                    {
                        bestMagnitude = magnitude;
                        bestRow = row;
                    }
                }
                if (bestMagnitude < 1e-14 || double.IsNaN(bestMagnitude) ||
                    double.IsInfinity(bestMagnitude))
                    return false;
                SwapRows(augmented, pivot, bestRow, Dimension + 1);
                double divisor = augmented[pivot, pivot];
                for (int column = pivot; column <= Dimension; column++)
                    augmented[pivot, column] /= divisor;
                for (int row = 0; row < Dimension; row++)
                {
                    if (row == pivot) continue;
                    double factor = augmented[row, pivot];
                    for (int column = pivot; column <= Dimension; column++)
                        augmented[row, column] -= factor * augmented[pivot, column];
                }
            }
            for (int i = 0; i < Dimension; i++)
            {
                solution[i] = augmented[i, Dimension];
                if (double.IsNaN(solution[i]) || double.IsInfinity(solution[i]))
                    return false;
            }
            return true;
        }

        private static bool TryCovariance(double[,] matrix, float damping,
            double residualVariance, out float[] covariance)
        {
            covariance = null;
            var augmented = new double[Dimension, Dimension * 2];
            for (int row = 0; row < Dimension; row++)
            {
                double diagonalScale = Math.Max(1.0, Math.Abs(matrix[row, row]));
                for (int column = 0; column < Dimension; column++)
                    augmented[row, column] = matrix[row, column];
                augmented[row, row] += damping * diagonalScale;
                augmented[row, Dimension + row] = 1.0;
            }
            for (int pivot = 0; pivot < Dimension; pivot++)
            {
                int bestRow = pivot;
                double bestMagnitude = Math.Abs(augmented[pivot, pivot]);
                for (int row = pivot + 1; row < Dimension; row++)
                {
                    double magnitude = Math.Abs(augmented[row, pivot]);
                    if (magnitude > bestMagnitude)
                    {
                        bestMagnitude = magnitude;
                        bestRow = row;
                    }
                }
                if (bestMagnitude < 1e-14 || double.IsNaN(bestMagnitude) ||
                    double.IsInfinity(bestMagnitude))
                    return false;
                SwapRows(augmented, pivot, bestRow, Dimension * 2);
                double divisor = augmented[pivot, pivot];
                for (int column = 0; column < Dimension * 2; column++)
                    augmented[pivot, column] /= divisor;
                for (int row = 0; row < Dimension; row++)
                {
                    if (row == pivot) continue;
                    double factor = augmented[row, pivot];
                    for (int column = 0; column < Dimension * 2; column++)
                        augmented[row, column] -= factor * augmented[pivot, column];
                }
            }

            // Edge covariance order is translation xyz followed by rotation xyz.
            covariance = new float[Dimension];
            covariance[0] = BoundedVariance(augmented[3, Dimension + 3] * residualVariance);
            covariance[1] = BoundedVariance(augmented[4, Dimension + 4] * residualVariance);
            covariance[2] = BoundedVariance(augmented[5, Dimension + 5] * residualVariance);
            covariance[3] = BoundedVariance(augmented[0, Dimension] * residualVariance);
            covariance[4] = BoundedVariance(augmented[1, Dimension + 1] * residualVariance);
            covariance[5] = BoundedVariance(augmented[2, Dimension + 2] * residualVariance);
            return true;
        }

        private static float BoundedVariance(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 100f;
            return Mathf.Clamp((float)Math.Abs(value), 1e-8f, 100f);
        }

        private static void SwapRows(double[,] matrix, int first, int second,
            int columns)
        {
            if (first == second) return;
            for (int column = 0; column < columns; column++)
            {
                double temporary = matrix[first, column];
                matrix[first, column] = matrix[second, column];
                matrix[second, column] = temporary;
            }
        }

        private static Vector3 ClampMagnitude(Vector3 value, float maximum)
        {
            float magnitude = value.magnitude;
            return magnitude > maximum && magnitude > 0f
                ? value * (maximum / magnitude)
                : value;
        }

        private static Quaternion QuaternionFromRotationVector(Vector3 vector)
        {
            float angle = vector.magnitude;
            if (angle < 1e-7f)
                return Normalize(new Quaternion(vector.x * 0.5f,
                    vector.y * 0.5f, vector.z * 0.5f, 1f));
            float half = angle * 0.5f;
            float scale = Mathf.Sin(half) / angle;
            return new Quaternion(vector.x * scale, vector.y * scale,
                vector.z * scale, Mathf.Cos(half));
        }

        private static Quaternion Normalize(Quaternion value)
        {
            float norm = Mathf.Sqrt(value.x * value.x + value.y * value.y +
                                    value.z * value.z + value.w * value.w);
            if (norm < 1e-8f || !Finite(norm))
                return Quaternion.identity;
            float inverse = 1f / norm;
            return new Quaternion(value.x * inverse, value.y * inverse,
                value.z * inverse, value.w * inverse);
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct Sample
        {
            internal readonly Vector3 Point;
            internal readonly Vector3 Normal;

            internal Sample(Vector3 point, Vector3 normal)
            {
                Point = point;
                Normal = normal;
            }
        }

        private sealed class NormalEquations
        {
            internal readonly double[,] Matrix = new double[Dimension, Dimension];
            internal readonly double[] Gradient = new double[Dimension];
            internal double ResidualSquared;
            internal int CorrespondenceCount;
        }

        private sealed class SourceSpatialIndex
        {
            private readonly Dictionary<VoxelKey, List<int>> _buckets = new();
            private readonly float _inverseCellSize;

            internal SourceSpatialIndex(Sample[] samples, float cellSize)
            {
                _inverseCellSize = 1f / cellSize;
                for (int i = 0; i < samples.Length; i++)
                {
                    VoxelKey key = Key(samples[i].Point);
                    if (!_buckets.TryGetValue(key, out List<int> bucket))
                    {
                        bucket = new List<int>();
                        _buckets.Add(key, bucket);
                    }
                    bucket.Add(i);
                }
            }

            internal int FindNearest(Vector3 point, Sample[] samples,
                float maximumDistanceSquared)
            {
                VoxelKey center = Key(point);
                int nearest = -1;
                float nearestDistance = maximumDistanceSquared;
                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    var key = new VoxelKey(center.X + x, center.Y + y,
                        center.Z + z);
                    if (!_buckets.TryGetValue(key, out List<int> bucket))
                        continue;
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        int sampleIndex = bucket[i];
                        float distance = (samples[sampleIndex].Point - point).sqrMagnitude;
                        if (distance < nearestDistance ||
                            distance == nearestDistance && sampleIndex < nearest)
                        {
                            nearest = sampleIndex;
                            nearestDistance = distance;
                        }
                    }
                }
                return nearest;
            }

            private VoxelKey Key(Vector3 point)
            {
                return new VoxelKey(Mathf.FloorToInt(point.x * _inverseCellSize),
                    Mathf.FloorToInt(point.y * _inverseCellSize),
                    Mathf.FloorToInt(point.z * _inverseCellSize));
            }
        }

        private readonly struct VoxelKey : IEquatable<VoxelKey>
        {
            internal readonly int X;
            internal readonly int Y;
            internal readonly int Z;

            internal VoxelKey(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public bool Equals(VoxelKey other)
            {
                return X == other.X && Y == other.Y && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is VoxelKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X;
                    hash = hash * 397 ^ Y;
                    return hash * 397 ^ Z;
                }
            }
        }
    }
}
