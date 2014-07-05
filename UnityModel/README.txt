
This directory contains what I *BELIEVE* to be the necessary source files
to work on the Part's model, minus the PartTools toolkit that you can
get from SQUAD.  If there is anything you need that is missing from here
let me know and I will try to get it to you.

Disclaimer:  I am a programmer.  I am not a graphic artist or modeler.
I don't have any experience with making things in Unity, so this could
be all wrong.

If you edit this, keep the following in mind:

1. Keep the scale roughly the same - don't make the part really big or small.

2. Currently the place where the part attaches to the parent part is:

   xyz(-0.075, 0.0, 0.0), direction negative X axis.

If you change this, please edit the part.cfg file's node_attach setting
to match the change.

3. Currently the relevant coordinates within the part model where the
C# code draws the laserbeam from are:

   Laserbeam origin point = xyz(0.0, -0.3, 0.0)
   Laser pointing direction = negative Y axis.

If you change either of the above two things, let me know so I can
change the C# code to match.  (I don't know how to make this a field
within the part.cfg file).

