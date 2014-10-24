Using LaserDist in kOS
======================

The eventual goal of using LaserDist is to aid in writing kOS scripts.
This document explains how it would be used in kOS.

**NOTE: As of this writing, the support in kOS for this is still being worked on, and what this document refers to does not work yet.**

*But as the person who is in fact implementing the feature in kOS that this
depends on, I have it on pretty good authority that this is how it will work
once it's been developed.*

Step 1 - Get a handle on the laserDist part
-------------------------------------------
As of right now there isn't a pretty way to do this.  That may be
improved later.  For now, you have to do this:

    SET LASERDISTMOD TO "dummy".
    LIST PARTS IN PARTLIST.
    FOR P in PARTLIST {
      FOR MODNAME in P:MODULES {
        IF MODNAME = "LaserDistModule" {
	  SET LASERDISTMOD TO P:LaserDistModule.
	}.
      }.
    }.

This is a bit ugly, but you only have to do it once up front.  After that
the variable LASERDISTMOD would be a handle to the laser dist module on
one of the parts of the craft.

To get a better query to specify which particular laserdist part you want,
better support in kOS will be needed for part queries.

Step 2 - Toggle the Laserdist module on and off.
------------------------------------------------

Assuming you've done the above work to store the laser dist module handle
in the variable LASERDISTMOD, you can then do this:

    LASERDISTMOD:SETFIELD("ENABLED") TO FALSE. // turn it off.
    LASERDISTMOD:SETFIELD("ENABLED") TO TRUE. // turn it on.
    LASERDISTMOD:SETFIELD("VISIBLE") TO FALSE. // Make it invisible even when enabled.
    LASERDISTMOD:SETFIELD("VISIBLE") TO TRUE. // Make it visible on screen when enabled.

Step 3 - Read the distance When it's on.
----------------------------------------

Once you turn the laser dist module On, then you presumably want to read the
distance.  That's easy - just do this:

    PRINT "DISTANCE IS CURRENTLY " + LASERDISTMOD:GETSUFFIX("DISTANCE").

It's a floating point number you can do math with, or comparisons.

Step 4 - Detect whether it's hitting something or not.
------------------------------------------------------

The name of the thing being hit by the laser is returned in the HIT suffix.
If it's not hitting anything, the value of HIT will be "<none>".

    SET HITNAME TO LASERDISTMOD:GETFIELD("HIT").
    IF HITNAME = "<none>" {
      PRINT "Laser is not hitting anything.".
    } ELSE {
      PRINT "LASER IS HITTING " + HITNAME.
    }.

It's also possible to detect that the laser isn't hitting anything by 
noticing if the :DISTANCE is -1.  -1 is what it returns when there's
no hit.

    SET D TO LASERDISTMOD:GETFIELD("DISTANCE").
    IF D < 0 {
      PRINT "Laser is not hitting anything.".
    }.

Step 5 - Being careful about :UPDATE_AGE
----------------------------------------

Sometimes the LaserDist mod takes longer than 1 universe Update to finish
calculating its answer.  This is a deliberate decision taken in order to
prevent slow frame rates in the game.  LaserDist only allows itself a very
small limited amount of milliseconds of time per Unity Update to perform
its work.  If it is taking longer than that to get an answer, it continues
the calculations in the next update in order to prevent KSP from getting
to choppy in its animation.

NOTE: THIS IS **NEVER** GOING TO BE A PROBLEM WHEN MEASURING CLOSE DISTANCES.
Updates always occur every single moment when the laser is aimed at nearby
objects within about 10km.  Slow updates only start happening when measuruing
distances to far away objects.

If you need to be certain that the value in the "DISTANCE" field is correct
in the current update, you can check the value of the :GEFIELD("UPDATE_AGE") field
to see how old the value you are reading in :GETFIELD("DISTANCE") actually is.  When
:GETFIELD("UPDATE_AGE") is zero, then the value of :DISTANCE has just been finished
being recalculated.  When :GETFIELD("UPDATE_AGE") is larger than zero, then it means
the value you are reading in :GETFIELD("DISTANCE") was calculated several updates 
ago and is getting "stale".  You can make your script wait until it sees
that UPDATE_AGE is zero before it trusts the value of DISTANCE.

### Behind the scenes: Why the need for UPDATE_AGE?

When there are polygons nearby to hit, LaserDist can return an answer right
away using a built-in feature that Unity (and most 3D systems) have called
a "RayCast" that detects if a ray intersects one of the polygons in the
rendering engine.  This is a very fast check that is sometimes done directly
in the video card's hardware.  But when aiming the laser at a planet
surface that's farther than about 10 Kilometers away, typically those
terrain polygons aren't actually loaded.  The number of polygons needed
to describe ALL the terrain of all the planet's visible surface from a 100km
orbit when each polygon is only a few square meters would be enormous and
would bring almost any video card to its knees.  Therefore KSP only creates
the polygons that are within about 10km or so of the current vessel.  For
terrain farther away, it switches to a different algorithm that visually
draws the terrain but doesn't make it into full fledged polygons.  The
problem this creates for LaserDist is that this distant terrain is effectively
"holographic" in the sense that nothing collides with it in the Unity engine,
and you can't use the video card's hardware to help find the raycast solution
for you.  Therefore when the laser doesn't hit a nearby polygon, the
LaserDist mod has to switch to a computationally expensive homemade numeric
iteration algorithm to try to search along the line finding where it first
goes under the holographic terrain.  If this algorithm was allowed to
run fully to completion every single animation frame, it would slow down
the frame rate of the game.  Therefore it's designed to only work toward
the answer in pieces a little bit at a time.  It may take as many as 10
updates before it settles on an answer, thus making the sampling rate of
getting answers slower when doing it from a great distance.

