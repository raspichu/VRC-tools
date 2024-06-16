# Unused Bone Remover Editor Script

## Overview
The Unused Bone Remover is an Editor script for Unity that allows you to remove unused bones from a GameObject that contains `SkinnedMeshRenderer` and `VRCPhysBone` components. This can be useful for optimizing and cleaning up models in your Unity projects, particularly for VRChat setups using VRChat SDK's dynamic bone system.

This script generates a clean new copy of the GameObject with only the necessary bones.



## Usage
1. **Installation:**
   - Place the `UnusedBoneRemover.cs` script in your Unity project's Editor folder (`Assets/Editor`).

2. **Using the Tool:**
   - Open Unity and select a GameObject in your Scene or Hierarchy that you want to clean up.
   - Right click on the GameObject in the Hierarchy tab and selecting `Unused Bone Remover`
        - You also can also execute the script by navigating to `Tools > Unused Bone Remover`.

3. **Result:**
   - The script will create a cleaned-up copy (`{GameObject.name}_clean`) of the selected GameObject.
   - Unused bones will be removed from the copied GameObject based on the bones used by `SkinnedMeshRenderer` and `VRCPhysBone` colliders.
   - Unused Physbone colliders will be also removed from the result.

4. **Notes:**
   - Ensure that your GameObjects have `SkinnedMeshRenderer` and/or `VRCPhysBone` components for the script to effectively remove unused bones.
   - The script deactivates the original GameObject temporarily to create the copy and restore its state afterward.

## License
This project is licensed under the MIT License

## Author
- [Pichu](https://github.com/raspichu)
