using SharpDX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public class BoneAttributesCalculator {
	private const float CubicCentimetersPerLiter = 1000;

	private readonly ChannelSystem channelSystem;
	private readonly BoneSystem boneSystem;
	private readonly Geometry geometry;
	private readonly SkinBinding skinBinding;

	public BoneAttributesCalculator(ChannelSystem channelSystem, BoneSystem boneSystem, Geometry geometry, SkinBinding skinBinding) {
		this.channelSystem = channelSystem;
		this.boneSystem = boneSystem;
		this.geometry = geometry;
		this.skinBinding = skinBinding;
	}

	private static float SignedTetrahedralVolume(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3) {
		Vector3 p10 = p1 - p0;
		Vector3 p20 = p2 - p0;
		Vector3 p30 = p3 - p0;
		return 1/6f * Vector3.Dot(Vector3.Cross(p10, p20), p30);
	}

	private MassMoment[] CalculateBoneMassMoments() {
		Debug.Assert(skinBinding.BoneWeights.Count == geometry.VertexCount);

		MassMoment[] massMoments = new MassMoment[boneSystem.Bones.Count];

		foreach (var quad in geometry.Faces) {
			if (quad.IsDegeneratedIntoTriangle) {
				throw new NotImplementedException("BoneAttributesCalculator only supports Quad faces");
			}
			
			for (int cornerIdx = 0; cornerIdx < Quad.SideCount; ++cornerIdx) {
				int vertexIdx = quad.GetCorner(cornerIdx);
				foreach (var boneWeight in skinBinding.BoneWeights.GetElements(vertexIdx)) {
					var bone = skinBinding.Bones[boneWeight.Index];

					Vector3 p1 = geometry.VertexPositions[quad.GetCorner(cornerIdx - 1)];
					Vector3 p2 = geometry.VertexPositions[vertexIdx];
					Vector3 p3 = geometry.VertexPositions[quad.GetCorner(cornerIdx + 1)];

					var boneCenter = bone.CenterPoint.GetValue(channelSystem.DefaultOutputs);

					float volume = SignedTetrahedralVolume(boneCenter, p1, p2, p3) / CubicCentimetersPerLiter;
					float mass = volume / 2; //assume a density of 1 kg per liter, half because each point is covered by two tetrahedra
					Vector3 position = (boneCenter + p1 + p2 + p3) / 4;
					Vector3 relativePosition = position - boneCenter;

					massMoments[bone.Index].AddInplace(mass, relativePosition);
				}
			}
		}
				
		return massMoments;
	}

	public float[] SumChildren(float[] boneMasses) {
		float[] totalBoneMasses = new float[boneSystem.Bones.Count];
		foreach (var bone in boneSystem.Bones) {
			for (var ancestor = bone; ancestor != null; ancestor = ancestor.Parent) {
				totalBoneMasses[ancestor.Index] += boneMasses[bone.Index];
			}
		}
		return totalBoneMasses;
	}
	
	private bool[] CalculateIkability() {
		bool[] areIkable = new bool[boneSystem.Bones.Count];

		//descendant of these bones are non-IKable
		HashSet<string> nonIkableDescendants = new HashSet<string> {
			"lFoot",
			"rFoot",
			"head",
			"lHand",
			"rHand"
		};

		//these bones (and an their children) are non-IKable
		HashSet<string> nonIkable = new HashSet<string> {
			"lPectoral",
			"rPectoral"
		};

		foreach (var bone in boneSystem.Bones) {
			bool isIkable;

			if (nonIkable.Contains(bone.Name)) {
				isIkable = false;
			} else if (bone.Parent == null) {
				isIkable = true;
			} else if (!areIkable[bone.Parent.Index]) {
				isIkable = false;
			} else if (nonIkableDescendants.Contains(bone.Parent.Name)) {
				isIkable = false;
			} else {
				isIkable = true;
			}

			areIkable[bone.Index] = isIkable;
		}

		return areIkable;
	}

	public BoneAttributes[] CalculateBoneAttributes() {
		bool[] areIkable = CalculateIkability();
		MassMoment[] massMoments = CalculateBoneMassMoments();
		float[] masses = massMoments.Select(massMoment => massMoment.Mass).ToArray();
		float[] massesIncludingDescendants = SumChildren(masses);

		BoneAttributes[] boneAttributes = new BoneAttributes[boneSystem.Bones.Count];
		for (int i = 0; i < boneAttributes.Length; ++i) {
			boneAttributes[i] = new BoneAttributes(areIkable[i], massMoments[i], massesIncludingDescendants[i]);
		}
		return boneAttributes;
	}

	public int[] CalculateFaceToBoneMap() {
		bool[] areIkable = CalculateIkability();
		Bone[] boneToIkableAncestorMap = new Bone[boneSystem.Bones.Count];
		foreach (var bone in boneSystem.Bones) {
			var ikableAncestor = areIkable[bone.Index] ? bone : boneToIkableAncestorMap[bone.Parent.Index];
			boneToIkableAncestorMap[bone.Index] = ikableAncestor;
		}

		var controlFaceToBoneMap = geometry.Faces
			.Select(quad => {
				//gather the bone weights across all vertices of the face
				var allBoneWeights = 
					skinBinding.BoneWeights.GetElements(quad.Index0)
					.Concat(skinBinding.BoneWeights.GetElements(quad.Index1))
					.Concat(skinBinding.BoneWeights.GetElements(quad.Index2))
					.Concat(skinBinding.BoneWeights.GetElements(quad.Index3));

				//map each bone to closest IKable ancestor
				var ikableBoneWeights = allBoneWeights
					.Select(boneWeight => {
						Bone bone = skinBinding.Bones[boneWeight.Index];
						Bone ikableBone = boneToIkableAncestorMap[bone.Index];
						return new BoneWeight(ikableBone.Index, boneWeight.Weight);
					});

				//sum the weights of each IKable ancestor
				var mergedBoneWeights = ikableBoneWeights.GroupBy(
					boneWeight => boneWeight.Index,
					boneWeight => boneWeight.Weight)
					.Select(group => new BoneWeight(group.Key, group.Sum(weight => weight)));

				//select the highest weighted IKable  bone
				var maxBoneWeight = mergedBoneWeights
					.OrderByDescending(boneWeight => boneWeight.Weight)
					.First();

				return maxBoneWeight.Index;
			})
			.ToArray();

		return controlFaceToBoneMap;
	}
}
