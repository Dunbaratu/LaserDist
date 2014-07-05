LaserDist
=========

[WIP, Plugin, Parts] LaserDist 0.1 for KSP 0.23.5, Alpha
--------------------------------------------------------

This is a vey small plugin.  It makes a KSP Part that
measures straight line distances by laser.

The "Beamer 100x Disto-o-meter" Part aims a laser in a line
and then measures the distance in meters to the first object
the laser hits.  The result is displayed in the right-click
menu for the part.

![LaserDist screenshot 1](readme_screenshot1.png)

The direction of the laser is whichever way the laser gun is
pointed when you mounted it on the craft, as demonstrated here:

![LaserDist screenshot 2](readme_screenshot2.png)

### Why?

The intended purpose of this part is to be used in conjunction with
the scripted autopilot [kOS](https://github.com/KSP-KOS/KOS), to
provide a way to for you to write scripted pilot software that can
see the distance to the ground (or anything else like a ship) along
the laser line.  The reason this can be useful is so you can detect
things like terrain slope and mountains in the way.  The default
radar altimiter in KSP only shows you the distance directly under
the craft.

In a nutshell, the purpose is to solve this problem:
![Laser Need Diagram](laser_need.png)

This mod can let you read the distance along the blue line in the diagram.

### Why isn't it inside kOS then?

There is more than one KSP mod project for the purpose of letting
users write scripted autopilots.  Another such project currently under
development is [Jebnix](https://github.com/griderd/Jebnix).

My goal is to make this part script-engine-agnostic so it works with
any such mod.  I've been working in kOS mostly, but I didn't want this
part to be kOS-specific because there's no particular reason it has
to be.

![LaserDist screenshot 1](readme_screenshot1.png)

### How do I use it from my script then?

That hasn't been written yet.  I figured I'd make the part first, and
then once it exists issues about how to integrate it can come later.

Its a bit unclear to me how to make this mod's reading fit the same exact
pattern as other sensor parts like the Gravioli detector and
the thermometer.  That is the goal.  There seems to be some undocumented
stuff going on with those parts, and my goal is to make this part behave
the same way.

But at the moment it still does work as a manual piloting instrument,
which is good enough for a 0.1 release.

### Part modeling help?

I am aware that the artwork on the model isn't pretty.  I'm a 
programmer, not a graphic artist, and I don't have experience
with things like Maya and Blender.  In fact I just made the model
by slapping together some stretched Cube and Cylnder objects in
Unity itself, without the aid of a modeling program.  The model
is good enough to work with, but I'd be happy to have someone
better at art redesign the model.  I included the model in
the github directory if you want to have a look.



