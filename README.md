# Unused Bone Remover Editor Script

## Overview

The Unused Bone Remover is an Editor script for Unity designed to remove unused bones from GameObjects containing `SkinnedMeshRenderer` and `VRCPhysBone` components. This tool is particularly useful for optimizing and cleaning up models in Unity projects, especially for setups intended for VRChat using VRChat SDK's dynamic bone system. The script generates a clean copy of the GameObject with only the necessary bones.

## Usage

### Installation:

1. **Manual Installation:**
   - Clone or download this repository.
   - Add the `UnusedBoneRemover.cs` script to your Unity project's `Assets/Editor` folder.

2. **Unity Package:**
   - Alternatively, download the `InspectorEnhancements.unitypackage` file from the [releases](https://github.com/raspichu/VRC-Unused-Bone-Remover/releases) section.
   - Import the package into your Unity project by double-clicking it or using `Assets -> Import Package -> Custom Package`.

### Using the Tool:

1. Open Unity and select a GameObject in your Scene or Hierarchy that you want to clean up.
2. Right-click on the GameObject in the Hierarchy tab and select `Unused Bone Remover`.
   - Alternatively, you can execute the script by navigating to `Tools -> Unused Bone Remover`.

### Result:

- The script will create a cleaned-up copy (`{GameObject.name}_clean`) of the selected GameObject.
- Unused bones will be removed from the copied GameObject based on the bones used by `SkinnedMeshRenderer` and `VRCPhysBone` colliders.
- Unused Physbone colliders will also be removed from the result.

### Notes:

- Ensure that your GameObjects have `SkinnedMeshRenderer` and/or `VRCPhysBone` components for the script to effectively remove unused bones.
- The script temporarily deactivates the original GameObject to create the copy and restores its state afterward.

## License

This project is licensed under the MIT License.

## Author

- [Pichu](https://github.com/raspichu)
