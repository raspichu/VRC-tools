# VRC Tools

## Overview

VRC-Tools is a series of tools designed to optimize, enhance and make your life easy in Unity projects for your VRChat avatars.


## Features and usages

### Unused Bone Remover

This tool is designed to remove unused bones from GameObjects containing SkinnedMeshRenderer and VRCPhysBone components.
The script generates a clean copy of the GameObject with only the necessary bones.

1. Select a GameObject in your Scene or Hierarchy that you want to clean up.
2. Right-click on the GameObject in the Hierarchy tab and select `Unused Bone Remover`.
   - Alternatively, you can execute the script by navigating to `Tools -> Unused Bone Remover`.

### Parameters to MA parameters

The Parameters to MA Parameters script transforms a parameter file from VRChat into a Modular Avatar (MA) parameter script.

1. Add a new component to an object and select `Pichu -> Parameter to MA`.
2. Insert the parameter file into the designated input box.
3. Press `Create`.

**Note:** If an MA parameter script already exists on the object, you have the option to add new parameters to the existing script or clear the list and add the new ones.

### Bulk Upload

The Bulk Upload tool simplifies the process of uploading multiple VRChat avatars in one go.

You need to have the VRChat SDK window open and logged in for it to work.

Avatars that don't have blueprint attached will give an error.

1. Open the window navigating to `Window -> Pichu -> Bulk Upload`
2. Press `Upload All (X)`

### Enforce Blendshape
The Enforce Blendshape tool ensures that a specified blendshape is consistently applied across the selected mesh, it will apply the blendshape to the mesh and remove it so no animation will change it.

1. Add the Enforce Blendshape component to your GameObject.
2. Select the mesh and the blendshapes that you want to have (Only the ones with >0 weight will show up)

### Path Deleter
The Path Deleter component allows you to remove relative paths from the avatar after building MA.

1. Add a new component to an object and select `Pichu -> Path Deleter`.
2. Type the relative path from the root of the avatar of the object you want to delete after building

## Installation
1. **VCC Listing**
   - Go to [My VRChat Creator Companion listing](https://raspichu.github.io/vpm-listing/)
   - Press "Add to VCC"
   - Go to your project in `VCC -> Manage Project`
   - Search and add `Pichu VRC tools`
     
2. **Manual Installation:**
   - Clone or download this repository.
   - Add the files to your project.

3. **Unity Package:**
   - Alternatively, download the `dev.raspichu.vrc-tools.unitypackage` file from the [releases](https://github.com/raspichu/VRC-Tools/releases) section.
   - Import the package into your Unity project by double-clicking it or using `Assets -> Import Package -> Custom Package`.

## License

This project is licensed under the MIT License

## Author

- [Pichu](https://github.com/raspichu)
