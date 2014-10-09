Changes per version for LaserDist (in reverse order)
======================================================

v0.4  KSP 0.25 compatibility update, and AVC support
---------------------------------------------------

Fixed:

* The feature to view the laserbeam inside the editor (VAB or SPH) had stopped working.  The part was behaving as if it can't detect electrical power when in the VAB anymore, and thus it though it was starved of power.  Fixed by bypassing the powerdrain check while in the editor.

New:

* Support for Automatic Version Checker mod. If you use KSP-AVC, you should be able to get update stats about LaserDist automatically now.

v0.3 Now ready for kOS when kOS Fields released?
-------------------------------------------------------------------

A lot has been fixed up and redone about the low-level numeric
approximation algorithm (the "PQS raycast solver").  That's the
bit that deals with the fact that none of KSP's built-in APIs 
actually find raycast intersections with the terrain when the
terrain is far away and thus not "fully" loaded.  (Even though
some are named as if they would, they don't.  They only find
intersection with the sea level sphere under the terrain.)

The list of changes are:

* Changes to the homebrewed PQS raycasting algorightm:
    - **tighter +/- error level**: When the terrain is far enough way not to be fully loaded, so that my PQS aproximater raycaster has to be used, the new "epsilon" is 2 meters.  (This is the accuracy level for the numeric approximizer.  When it gets a value within 2 meters of the right answer it quits and gives you that.  It used to be 5 meters).
    - **less hogging of the limited CPU time**: When it has to fallback to the PQS approximate raycaster, it now limits its CPU usage based on how LONG it's taking rather than on other guesses.  It actually watches a stopwatch and limits itself to a thin slice of the total time one physics update is meant to take - so other mods and the main game can have their time too.
    - **algorithm can now spread across updates**: To allow less hogging of the CPU time, it now can store its state in the algorithm and then come back to it in the next update tick.  So when the time is exceeded and it doesn't have a good answer yet, it now waits for the next update to pick up where it left off.  This means that getting an answer can sometimes now take a few updates to obtain.  A manual player won't see the difference, but an autopilot script needs to know about this.
* New fields on the rightclick panel:
    - **CPU hog** - a number from 2 to 20 - editable - This lets you set the balance between getting answers fast versus being nice to the rest of the game and trying not to take too much time during an animation frame.  If you want faster updates to the Distance readout, set this higher.  If the mod is stealing your framerate and becoming a problem, set this lower.  If you want to permanently change this value and not constantly adjust it, change the setting ```CPUGreedyPercent``` in the part.cfg file for the laser.
    - **Update Age** - integer - readonly - How many animation frame updates has it been since the value in the Distance field was up to date?  If this value is bigger than zero, then that means you are looking at slightly stale data.  In normal operation it should be zero when near the terrain, and then when higher up it will spin from 0 to 1 to 2 to 3 to 4 or so and then back to 0 again - very fast.  This lets you know how slow your updates are from the solver.  If you want quicker updating, you have to set **CPU hog** higher.

v0.2 Now does crudimentary PQS terrain hitting.
-------------------------------------------------------------------

Now it can find hits on the terrain that's too far away to be loaded -
but the terrain hits are very fuzzy numbers - The algorithm to find
the hits takes a lot of iterations and I'm nervous about stealing too
many clock cycles from other mods, so I've accepted a bit of inaccuracy
to keep the execution fast.

v0.1p5 Literally nothing new but compiled against KSP 0.24
-------------------------------------------------------------------

There is literally no difference in the code in the mod - this is
just compiled using the newer 0.24 DLL. It seems compatible and
appears to "work" exactly as well as it did before.


v0.1p4 Electric charge updated and beam destruction enabled
-------------------------------------------------------------------

Now the beam picture goes away when part is destroyed, and the
electric charge is actually being used.

(NOTE: First release with a mention on the KSP forums.)


v0.1p2 Still working - not ready for prime-time.
-------------------------------------------------------------------

Still a work in progress - added some code to try to deal with the
problem of terrain using the pqscontroller, but it's not working yet.

Also, be careful reloading vessels that have been saved - there's
something wrong there that seems to blow up vessels contianing the part.


v0.1p1 First attempt (really just testing the github release interface)
---------------------------------------------------------------------

Still only works for manual flight, and only when low to the surface of
the planet (numbers being reported falsely when high up. I have a few
ideas what might be causing that but I'm ready to call it a night and I
want to get something out now.
