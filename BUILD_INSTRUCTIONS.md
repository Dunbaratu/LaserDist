Instructions for a contributor
==============================

To compile and package the project
----------------------------------

Use a C# development environment capable of Mono or .NET.  I used SharpDevelop.

### For Part Modeling:

I admit to being mostly ignorant of the world of 3D modeling and the
associated graphic artist skills needed to be good at it.  Please feel
free to improve the part model.  To work on the part model and animation,
see the files stored in the ```UnityModel``` subdirectory.  They are there
just as a "good enough" placeholder to get started on the code.

For further information, see the file called

* UnityModel/README.txt


### For Programming:

Add these as references to your project, from the KSP distribution folders: (Assume "$KSP_HOME" is the location you've installed Kerbal Space Program.)

* $KSP_HOME/KSP_Data/Managed/Assembly-CSharp.dll
* $KSP_HOME/KSP_Data/Managed/UnityEngine.dll

Build the project using the project file src/LaserDist/LaserDist.csproj

### To Package the ZIP file, and/or install to your own installation:

* Have gitbash shell (or regular bash if you're on Unix/Mac) installed on your machine.
* Have the 7zip ZIP file maker installed, or an equivalent that can do ZIP files from the command line.
* Edit the file makePackage.sh to change these settings at the top of the file:
  * $CMD_ZIP
  * $INSTALL_GAME_DIR
  * $DO_INSTALL
* From the base directory of this project, run this at the bash commandline:
  * bash ./makePackage.sh

