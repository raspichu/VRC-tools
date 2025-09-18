# VRC Tools

## Overview

VRC-Tools is a series of tools designed to optimize, enhance and make your life easy in Unity projects for your VRChat avatars.


## Features and usages

### Unused Bone Remover

This tool is designed to remove unused bones from GameObjects containing SkinnedMeshRenderer and VRCPhysBone components.
The script generates a clean copy of the GameObject with only the necessary bones.

1. Select a GameObject in your Scene or Hierarchy that you want to clean up.
2. Right-click on the GameObject in the Hierarchy tab and select `Pichu -> Unused Bone Remover`.
   - Alternatively, you can execute the script by navigating to `Tools -> Pichu -> Unused Bone Remover`.

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

### Disable and Set Editor-Only

This tool toggles the active state and tag of selected GameObjects. It remembers the original tag and reverts to it when toggled back.

1. Select the GameObject(s) in your scene or hierarchy.
2. Navigate to `Tools -> Pichu -> Disable and Set Editor-Only` or press the shortcut `Shift + A`.
3. The script will disable the GameObjects and set their tag to `EditorOnly`. When toggled back, it will restore the original tag and active state.

### Component Remover (Play Mode Only)

This tool allows you to remove specific components or GameObjects from your avatar during Play Mode, while keeping them intact in Build Mode. It's ideal for cleaning up unnecessary components or testing without permanently removing anything.

1. Add a new component to an object and select `Pichu -> Component Remover Play Mode`.
2. Drag and drop the components or GameObjects you want to remove during Play Mode into the list.
3. When you enter Play Mode, the specified components or GameObjects will be automatically removed.
4. The components or GameObjects won't be deleted when building the avatar.

**Note:** The tool provides a warning if you attempt to add a `Transform` component to the list.

### Material replacer

This tool allows you to easily replace all the materials that are inside of a GameObject.
1. Right-click on a GameObject and select `Pichu -> Material replacer`
   - Alternatively, open the window via `Window -> Pichu -> Unused Bone Remover`.
2. Select the new material and choose which materials from the list you want to replace.

### Invert Texture

This tool allows you to easily invert one or multiple textures (black becomes white, white becomes black), which is especially useful for masks.

1. Right-click on one or more textures in the **Project window** and select `Pichu -> Invert Texture`
The tool will automatically create a new texture for each selected texture in the same folder, with `_inverted` appended to the filename.

### UnityPackage Auto Sorter

This tool automatically opens a window after importing a `.unitypackage`, allowing you to sort the imported assets into a chosen category while preserving their internal folder structure.
*(This tool disabled by default, Enable it by going to `Tools -> Pichu -> Enable Sort Imported Package`)*

1. Import a `.unitypackage` by double-clicking it or via `Assets -> Import Package -> Custom Package`.
2. After the import completes, the **Package Sorter** window will appear automatically.
3. Select a category (`Clothes`, `Models`, `Other`) for the imported assets.
4. Press **Sort**. Only the assets from the imported package will be moved, preserving folder structure.

### Material Property Finder

This tool lets you search and inspect all materials on a selected GameObject, including its children. 
You can filter by a specific property and view which materials have it.

1. Select a GameObject in your Scene or Hierarchy.
2. Open the tool via:
   - Right-click → `Pichu -> Material Property Finder`
   - Or `Tools -> Pichu -> Material Property Finder`
3. Enter the property name you want to search for.
4. Press **Search** to populate the list of materials.



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
