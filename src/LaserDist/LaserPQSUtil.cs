/*
 * Created by SharpDevelop.
 * User: Dunbaratu
 * Date: 7/7/2014
 * Time: 7:54 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LaserDist
{
    enum LevelBound { LOWEST, HIGHEST };
    
    /// <summary>
    /// The PQS calculating tool to go with this laser distometer.
    /// Create a new instance of this per laser dist meter.
    /// </summary>
    public class LaserPQSUtil
    {
        private bool debugMsg = false;

        // These values remember what the last state of the solver was from the
        // previous Update that called it.  To be nice to other mods and the KSP
        // system itself, sometimes LaserDist will deliberately choose not to
        // finish its entire calculation in a single Update tick, deferring some
        // of the work until the next update.  These are the variables that track
        // the information from the previous update so it can pick up where it left
        // off last time.  It may take several updates to get a final value within
        // the desired epsilon.
        // ------------------------------------------------------------------------
        
        /// <summary>
        /// How many Unity Updates has it been since the last time the value coming out
        /// of this class's raycaster was up to date and correct?  If it's zero, then the value
        /// is up to date and correct for this Update.  If it's, for example, 3, then
        /// that would mean that 3 updates ago it was correct, but it's been spending
        /// the last 2 updates since then still working on a new answer and the answer
        /// it's got at the moment shouldn't be trusted yet.
        /// </summary>
        public int UpdateAge {get; private set;}
        
        /// <summary>
        /// Returns whatever the previous fully "done" answer from the pqs RayCast was.
        /// use this when RayCast claims it's not done calculating yet.
        /// </summary>
        public double PrevDist {get; private set;}
        /// <summary>
        /// Returns whatever the previous fully "done" answer's success code from the pqs RayCast was.
        /// use this when RayCast claims it's not done calculating yet.
        /// </summary>
        public bool PrevSuccess {get; private set;}
        /// <summary>
        /// Returns whatever the previous fully "done" answer's hit body from the pqs RayCast was.
        /// use this when RayCast claims it's not done calculating yet.
        /// </summary>
        public string PrevBodyName {get; private set;}

        // These are parameters to methd numericPQSSolver() where it should continue from the next time:
        // . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . .
        /// <summary>body of the terrain</summary>
        private CelestialBody hitBody;
        /// <summary>start of this line segment of the ray</summary>
        private Vector3d origin;
        /// <summary>direction of the ray - must be a unit vector starting at origin</summary>
        private Vector3d pointingUnitVec;
        /// <summary>length of this line segment of the ray</summary>
        private double dist;
        /// <summary>Number of slices to try to cut the line segment into</summary>
        private int slices;
        /// <summary>has a hit already been found and it's just being narrowed down more precisely?</summary>
        private bool honingSuccess;
        
        /// <summary>For measuring how long the current raycast solve chunk has been going.</summary>
        private System.Diagnostics.Stopwatch rayCastTimer;
        
        // These static settings tweak the numeric approximation algorithm:
        // ------------------------------------------------------------------

        /// <summary>
        /// Add this to the max distance to check for to the ground hit, to ensure that the
        /// ground hit will be inside the range even if the craft moves away from the
        /// ground a little bit between updates:
        /// </summary>
        private static double distUpperBoundFudge = 1000.0;
        
        /// <summary>
        /// Number of meters of error that's considered "good enough" to stop the algorithm:
        /// Because this is only used for terrain that's very far away, and also the PQS
        /// predicted terrain won't exactly match the actual terrain polygons that it gets
        /// modelled with when you get closer, making it more accurate is misleading anyway.
        /// </summary>
        private static double epsilon = 2;
        
        /// <summary>
        /// How many milliseconds will I allow myself to use during a single Unity Update call?
        /// When this value gets exceeded, I will flag my result so far as being not good enough
        /// (by incrementing UpdateAge so it's not zero), and also remember my current state
        /// so I can continue from it on the next update.  If I do get down to an answer within
        /// epsilon before this time runs out, then I will set UpdateAge to zero to indicate that
        /// the current answer is a good one, and then return.
        /// </summary>
        private double millisecondCap;
        
        /// <summary>
        /// Number of slices to typically divide the line segment into per recursion level,
        /// although some circumstances can change this:
        /// </summary>
        private static int defaultSlices = 50;
        
        /// <summary>
        /// I refuse to allow my self to have more than this much of the amount of time a
        /// single physics tick is meant to take to calculate during one update.  if the
        /// algorithm is taking longer than this, then interrupt it where it is, save the
        /// state of it, and come back to finish the calcultion next update.
        /// This shouild be expressed as a number betwen 0 and 1.  i.e. 0.05 for "5%".
        /// </summary>
        public double tickPortionAllowed = 0.05;
        
        private static double lightspeed = 299792458; // in m/s

        private double lastFailTime = 0.0;
        private Part laserPart;
        
        public LaserPQSUtil( Part p )
        {
            laserPart = p;
            PrevDist = 0;
            PrevSuccess = false;
            Reset();
        }
        
        /// <summary>
        /// Tell the program to stop trying to continue the previous partial
        /// search and just start a new one from scratch.
        /// </summary>
        public void Reset()
        {
            UpdateAge = 0;
            this.honingSuccess = false;
        }

        /// <summary>
        /// Perform the raycast using the PQS solver's ideal terrain calculation rather
        /// than the actual terrain polygons.  This can only give a numerical approximation
        /// answer by recursive guessing, but it should be good enough.
        /// </summary>
        /// <param name="origin">Unity World coords of the ray start</param>
        /// <param name="rayVec">Unity World coords direction vector relative to the origin</param>
        /// <param name="hitBody">Returns the CelestialBody that was hit, i.e. Kerbin</param>
        /// <param name="dist">Returns the disance in meters from the origin to the hit</param>
        /// <returns>True if there was a hit, False if there wasn't</returns>
        public bool RayCast( Vector3d origin, Vector3d rayVec, out CelestialBody hitBody, out double dist )
        {
            millisecondCap = 1000.0 * (double)GameSettings.PHYSICS_FRAME_DT_LIMIT * tickPortionAllowed;
            rayCastTimer = new System.Diagnostics.Stopwatch();
            rayCastTimer.Reset();
            rayCastTimer.Start();

            bool done = true;

            double distanceUpperBound = -1.0;
            double distanceLowerBound = -1.0;
            bool didHit = false;
            hitBody = null;
            dist = -1.0;
                
            if( UpdateAge > 0 )
            {
                Vector3d pointingUnitVec = rayVec.normalized;
                double terrainDist;
                bool success = false;
                if( debugMsg ) Debug.Log( "Starting continuation numericPQSolver");
                success = numericPQSSolver(
                    out terrainDist, out done, this.hitBody, this.origin, this.pointingUnitVec, this.dist, defaultSlices );
                hitBody = this.hitBody;
                if( success && done)
                {
                    didHit = true;
                    Vector3 destinationPoint = this.origin + terrainDist*this.pointingUnitVec;
                    dist = (destinationPoint - origin).magnitude;
                }
            }
            else
            {
                // Start off with the lowest level hit.
                didHit = raySphereLevelCast( origin, rayVec, null, out hitBody, out distanceLowerBound, out distanceUpperBound );
                if( didHit )
                {
                    dist = distanceUpperBound;
                }
                if(! didHit)
                {
                    hitBody = null;
                    dist = -1;
                    distanceUpperBound = -1; 
                }
                distanceUpperBound += distUpperBoundFudge;
            
                // Only bother doing the expensive check with the PQS terrain when in the sphere of
                // influence of the hitBody - don't bother for hits on distant bodies - for them
                // the distanceUpperBound hit is good enough:
                if( (! didHit) || laserPart.vessel.GetOrbit().referenceBody == hitBody )
                {
                    // Begin the recursion for the more computationally expensive PQS numeric solver:
                    // ------------------------------------------------------------------------------
                    Vector3d pointingUnitVec = rayVec.normalized;
                    double terrainDist;
                    bool success = false;
                    hitBody = laserPart.vessel.GetOrbit().referenceBody;

                    if( debugMsg ) Debug.Log( "Starting fresh new numericPQSolver");

                    // Start the ray at the lower bound of where the hit might be:
                    Vector3d closerOrigin = origin + distanceLowerBound*pointingUnitVec;

                    success = numericPQSSolver(
                        out terrainDist, out done, hitBody, closerOrigin, pointingUnitVec, distanceUpperBound, defaultSlices );
                    if( success && done )
                    {
                        didHit = true;
                        Vector3 destinationPoint = closerOrigin + terrainDist*pointingUnitVec;
                        dist = (destinationPoint - origin).magnitude;
                    }
                }
            }

            rayCastTimer.Stop();
            
            if( done )
            {
                PrevDist = dist;
                PrevSuccess = didHit;
                PrevBodyName = hitBody.name;
                Reset();
                if( debugMsg ) Debug.Log( "LaserPQSUtil.RayCast Returning a DONE state, having taken " +
                                         Math.Round(rayCastTimer.Elapsed.TotalMilliseconds,3) + " millis with answer " +
                                         didHit+":"+Math.Round(dist,2));
            }
            else
            {
                if( debugMsg ) Debug.Log( "LaserPQSUtil.RayCast Returning UNDONE state, having taken " + 
                                         Math.Round(rayCastTimer.Elapsed.TotalMilliseconds,3) + " millis with answer " + 
                                         didHit+":"+Math.Round(dist,2));
                ++UpdateAge;
            }

            return didHit;
        }

        /// <summary>
        /// This routine was originally meant to actually do the raycast entirely by itself,
        /// but the KSP API method pqs.RayIntersection() does not do what it sounds like it does.
        /// It only finds the sea-level intersection, not the terrain intersection.  So now this
        /// method is only the initial starting point of the algorithm - it just finds the sea level
        /// intersect that's under the actual terrain.
        /// </summary>
        /// <param name="origin">Location to start the ray from</param>
        /// <param name="rayVec">A vector describing the direction of the ray relative from origin</param>
        /// <param name="inBody">The body to check for.  Pass in null to try all the bodies in the game.</param>
        /// <param name="hitBody">The body that the hit was found for (=inBody if inBody wasn't null).</param>
        /// <param name="lowerBoundDist">The minimum possible distance the terrain hit might be found.</param>
        /// <param name="upperBoundDist">The maximum possible distance the terrain hit might be found.</param>
        /// <returns>True if there is a hit that seems likely, where the ray intersects the area between a body'd radiusMin and radiusMax.</returns>
        private bool raySphereLevelCast(
            Vector3d origin,
            Vector3d rayVec,
            CelestialBody inBody,
            out CelestialBody hitBody,
            out double lowerBoundDist,
            out double upperBoundDist)
        {
            if( debugMsg ) Debug.Log( "raySphereLevelCast( "+origin+","+rayVec+","+inBody+"(out hitBody),(out dist));" );

            List<CelestialBody> bodies;

            if (inBody == null)
            {
                if( debugMsg ) Debug.Log( "raySphereLevelCast checking all bodies." );
                bodies = FlightGlobals.Bodies;
            }
            else
            {
                if( debugMsg ) Debug.Log( "raySphereLevelCast checking body "+inBody+"." );
                bodies = new List<CelestialBody>();
                bodies.Add(inBody);
            }

            double bestLowerHitDist = -1.0;
            double bestUpperHitDist = -1.0;
            CelestialBody bestHitBody = null;
            double hitDist = -1.0;
            double now = Planetarium.GetUniversalTime();
            
            // For each body in the game, find if there's a hit with its surface calculator,
            // and if there is, then keep the hit that's closest (just in case two
            // bodies are in line of sight where a ray hits both of them, we want the
            // nearer of those hits.)
            foreach( CelestialBody body in bodies )
            {
                if( debugMsg ) Debug.Log( "raySphereLevelCast Now checking for "+body+"." );
                PQS pqs = body.pqsController;
                if( pqs != null )
                {
                    upperBoundDist = -1;
                    
                    double nearDist;
                    double farDist;
                    
                    // The ray must at least intersect the max radius sphere of the body or it can't be a hit.
                    // If it does intersect the max radius sphere then the real hit must be between the two intersects
                    // (near and far) of that sphere.
                    bool upperFound = GetRayIntersectSphere( origin, rayVec, body.position, body.pqsController.radiusMax, out nearDist, out farDist);
                    if( upperFound )
                    {
                        upperBoundDist = farDist;
                        lowerBoundDist = nearDist;
                        
                        // If the ray also hits the min radius of the sphere of the body, the the real hit must be bounded by
                        // the near hit of that intersect:
                        bool lowerFound = GetRayIntersectSphere( origin, rayVec, body.position, body.pqsController.radiusMin, out nearDist, out farDist);
                        if( lowerFound )
                        {
                            upperBoundDist = nearDist;
                        }
                        
                        Vector3d vecToUpperBound = upperBoundDist * rayVec;
                        // Check to see if the hit is in front of the ray instead of behind it:
                        if( Vector3d.Angle( vecToUpperBound, rayVec ) <= 90 )
                        {
                            if( bestUpperHitDist < 0 || bestUpperHitDist > hitDist )
                            {
                                bestLowerHitDist = lowerBoundDist;
                                bestUpperHitDist = upperBoundDist;
                                bestHitBody = body;
                            }
                        }
                    }
                }
            }
            hitBody = bestHitBody;
            upperBoundDist = bestUpperHitDist;
            lowerBoundDist = bestLowerHitDist;
            bool hitFound = (upperBoundDist > 0);
            if( hitFound )
            {
                 // A hit has to be maintained steadily, long enough to to last a full lightspeed
                 // round trip, or it doesn't really count as a hit yet:
                if( now < lastFailTime + ((upperBoundDist*2)/lightspeed) )
                {
                    hitFound = false;
                }
            }
            else
            {
                lastFailTime = now;
            }
            if( debugMsg ) Debug.Log( "raySphereLevelCast returning "+hitFound+", out hitBody="+hitBody+", out dist="+upperBoundDist);
            return hitFound;
        }
        
        /// <summary>
        /// This is the recursive function that implements the numeric terrain hit solver.
        /// The exact algorithm is too wordy to explain here in a text comment.
        /// See this markdown file on github for the full explanation:
        ///     doc/Recursive_Numeric_Terrain_Hit.md
        /// </summary>
        /// <param name="newDist">The "return value" (the actual return is bool, this is the distance if return is true).</param>
        /// <param name="done">returns true if the algorithm came to a final answer, false it if needs more time.</param></param>
        /// <param name="hitBody">body of the terrain</param>
        /// <param name="origin">start of this line segment of the ray</param>
        /// <param name="pointingUnitVec">direction of the ray - must be a unit vector starting at origin</param>
        /// <param name="dist">length of this line segment of the ray</param>
        /// <param name="slices">Number of slices to try to cut the line segment into</param>
        /// <returns>True if there was a hit</returns>
        private bool numericPQSSolver(
            out double newDist,
            out bool done,
            CelestialBody hitBody,
            Vector3d origin,
            Vector3d pointingUnitVec,
            double dist,
            int slices )
        {
            // Some bodies have no PQS collider - like the sun.  For them they have no surface and this doesn't work:
            if (hitBody.pqsController == null)
            {
                newDist = -1;
                done = true;
                return false;
            }
            
            if( debugMsg ) Debug.Log( "numericPQSSolver( (out),(out),"+hitBody.name+", "+origin+", "+pointingUnitVec+", "+dist+", "+slices+");");
            bool success = false;
            bool continueNextTime = false;
            bool hasOcean = hitBody.ocean;
            int i;
            double lat;
            double lng;
            double segmentLength = 0.0;
            newDist = dist;
            int slicesThisTime = slices;
            Vector3d samplePoint = origin;
            Vector3d prevSamplePoint;
            if( dist <= epsilon )
            {
                continueNextTime = false;
                success = this.honingSuccess;
                newDist = dist;
                if( debugMsg ) Debug.Log( "dist is now small enough to quit.  continue="+continueNextTime+", success="+success );
            }
            else
            {
                // We already know i=0 is above ground, so start at i=1:
                for( i = 1 ; i <= slicesThisTime ; ++i )
                {
                    prevSamplePoint = samplePoint;
                    samplePoint = origin + (i*(dist/slicesThisTime))*pointingUnitVec;
                    segmentLength = (samplePoint - prevSamplePoint).magnitude;
                
                    lat = hitBody.GetLatitude( samplePoint );
                    lng = hitBody.GetLongitude( samplePoint );

                    var bodyUpVector = new Vector3d( 1, 0, 0 );
                    bodyUpVector = QuaternionD.AngleAxis( lat, Vector3d.forward/*around Z axis*/ ) * bodyUpVector;
                    bodyUpVector = QuaternionD.AngleAxis( lng, Vector3d.down/*around -Y axis*/ ) * bodyUpVector;

                    double groundAlt = hitBody.pqsController.GetSurfaceHeight( bodyUpVector ) - hitBody.Radius;
                    double samplePointAlt = hitBody.GetAltitude( samplePoint );
                
                    if( samplePointAlt <= groundAlt || (hasOcean && samplePointAlt < 0) )
                    {
                        if( debugMsg ) Debug.Log( "Found a below ground: samplePointAlt="+samplePointAlt + ", groundAlt="+groundAlt );
                        success = true;
                        this.honingSuccess = true;
                        double subSectionDist;
                        bool subDone;
                        numericPQSSolver( out subSectionDist, out subDone, hitBody, prevSamplePoint, pointingUnitVec,
                                          segmentLength, slices );
                        continueNextTime = ! subDone;
                        newDist = ((i-1)*(dist/slicesThisTime)) + subSectionDist;
                        break;
                    }
                    if( rayCastTimer.Elapsed.TotalMilliseconds > millisecondCap )
                    {
                        if( debugMsg ) Debug.Log( "Ran out of milliseconds: " + rayCastTimer.Elapsed.TotalMilliseconds + " > " + millisecondCap );
                        this.hitBody = hitBody;
                        this.origin = prevSamplePoint - pointingUnitVec*20; // back up 20 meters because the planet will move some
                        this.pointingUnitVec = pointingUnitVec;
                        this.dist = segmentLength * (slicesThisTime - i + 2);
                        this.slices = slices;                    
                        continueNextTime = true;
                        break;
                    }
                }
                if( debugMsg ) Debug.Log( "numericPQSSolver after " + Math.Round(rayCastTimer.Elapsed.TotalMilliseconds,3) + " millis i = "+ i + " and continueNextTime=" + continueNextTime );
            
                if( i > slicesThisTime && segmentLength > epsilon && !continueNextTime )
                {
                    // The above loop got to the end without finding a hit, and the length of
                    // the line segments is still not so small as to be time to give up yet.
                    // Before giving up, it might be the case that there's a hit under the ground
                    // in-between the sample points that were tried, like this:
                    // 
                    //                            __
                    //                           /  \
                    // *----------*----------*--/----\--*----------*----------*----------*----
                    //                  _   ___/  ^   \     ____
                    //  _____      ____/ \_/      |    \___/    \        _________
                    //       \____/            hit in            \______/         \__________
                    //                      between the
                    //                     sample points
                    bool subDone = false;
                    if( debugMsg ) Debug.Log( "numericPQSSolver recursing.");
                    success = numericPQSSolver( out newDist, out subDone, hitBody, origin, pointingUnitVec,
                                                dist, 2*slices );
                    continueNextTime = ! subDone;
                }
            }
            done = ! continueNextTime;
            if( debugMsg ) Debug.Log( "numericPQSSolver returning "+ success+ " dist="+newDist+" done="+done );
            return success;
        }
        
        /// <summary>
        /// A debugging tool that was used to see how fast the PQS altitude detector actually is.
        /// It spams a LOT of random lat/long queries at the PQS solver.  You call it once per update
        /// to see how badly it lags the game.  This was used to experimentally find the best
        /// value for defaultSlices given that the goal is to be polite to other mods trying to use
        /// the same limited Update timeslice.  The goal was to make the algorithm only take about 2-3
        /// milliseconds.  This method is only used when debugging.
        /// </summary>
        /// <param name="numQueries">The number of PQS queries to try to spam at the API.</param>
        public void StressTestPQS(CelestialBody body, int numQueries)
        {
            System.Random randGen = new System.Random(); // "System." prefix because there's a UnityEngine.Random.
            double lat;
            double lng;
            double alt;
            
            for( int i = 0 ; i < numQueries ; ++i )
            {
                lat = randGen.NextDouble();
                lng = randGen.NextDouble();
                
                // Convert the lat/lng into the coords pqs solver expects (this is similar work
                // as will be done by the actual algorithm, so it's important to the stress test):
                var bodyUpVector = new Vector3d(1,0,0);
                bodyUpVector = QuaternionD.AngleAxis(lat, Vector3d.forward/*around Z axis*/) * bodyUpVector;
                bodyUpVector = QuaternionD.AngleAxis(lng, Vector3d.down/*around -Y axis*/) * bodyUpVector;
                
                alt = body.pqsController.GetSurfaceHeight( bodyUpVector ) - body.Radius ;                
            }
        }
        
        /// <summary>
        /// Homemade routine to calculate the intersection of a ray with a sphere.
        /// </summary>
        /// <param name="rayStart">position of the ray's start</param>
        /// <param name="rayVec">vector describing the ray's look direction</param>
        /// <param name="center">position of the sphere's center</param>
        /// <param name="radius">length of the sphere's radius</param>
        /// <param name="nearDist">returns the distance from rayStart to the near intersect</param>
        /// <param name="farDist">returns the distance from raystart to the far intersect</param>
        /// <returns>True if intersect found, false if no intersect was detected (in which case nearDist and farDist are both returned as -1.0)</returns>
        private bool GetRayIntersectSphere( Vector3d rayStart, Vector3d rayVec, Vector3d center, double radius, out double nearDist, out double farDist)
        {
            if( debugMsg ) Debug.Log( "GetRayIntersectSphere("+rayStart+","+rayVec+","+center+","+radius+",(out),(out));" );

            // The math algorithm is explained in this long ascii art comment:
            //
            // (original ascii art circle copied from ascii.co.uk/art/circle)
            // 
            //                       ,,----~""""~----,,
            //                  ,---""'              `""--,
            //      /     L2 ,--!"         C1            "~--, L1         L0          P0
            //     <--------@----------------@----------------@-------------@<==========@---
            //      \    ,-!"                :            __- "~-,    | theta  ___---
            //          ,|"                  :          _-      "|,    \ ___---
            //         ,|'                   :     r __-         `|___---
            //        ,|'                    :     _-        ___---|,
            //        -'                     :  __-    ___---      `-
            //        |                    C0:_- ___---             |
            //        |                      @---                   |
            //        |                       \                     |
            //        |                        \                    |
            //        ~,                        \                  ,!
            //        `|,                        \ r              ,|' 
            //         `|,                        \              ,|'
            //          `|-                        \             |'
            //           `~--                       \         --!'
            //             "~--                      \      --~"
            //               `"~--,                   \ ,--!"'
            //                  `"~|--,             ,--\!"'
            //                       ``""~~-----!!""''
            // 
            // Knowns:
            //    P0 = Position of ray start
            //    L0 = Position of "lookat" that represents the ray direction.
            //    C0 = Position of sphere center
            //    r = radius of sphere
            // 
            // Not known, but trivial to calculate with built-ins:
            //    theta = angle between the ray and a line to the sphere center.
            // 
            // Unknowns:
            //    L1, L2 = the interesect points - finding these is the goal.
            //    C1 = The point on the ray in the center of its chord through the sphere
            //         It represents the point where the line from center to the ray is
            // 	perpendicular with the ray - so it can be used to form right triangles
            // 	which helps because it means we can use trig.
            // 
            // Notation used (since ascii can't do some things well):
            // 
            //    _____\ = the "vector" symbol, vector from P0 to L0.
            //    P0.L0
            // 
            //    |   |   = The "absolute value" symbol, or "vector magnitude" symbol.
            //    |   |
            // 
            // We'll use theta to do some trig, so finding it is key.  It's just the
            // angle between these two vectors, which is trivial to calculate with
            // a Unity built-in or with a dot-product:
            // 
            //    theta = angle between | _____\ |   and   | _____\ |
            //                          | P0.L0  |         | P0.C0  |
            // 
            // 
            // Length of the two legs of the large triangle can be found by simple trig:
            // 
            //    | _____\ |                   | _____\ |
            //    | C0.C1  |   =  sin(theta) * | P0.C0  |
            // 
            //    | _____\ |                   | _____\ |
            //    | P0.C1  |   =  cos(theta) * | P0.C0  |
            //                         
            // At this point if the length of C0.C1 is bigger than r, we can abort here
            // as there are no solutions, because it means the ray is entirely outside
            // the sphere.
            // 
            // Length of the distance from C1 to L1 can be found by Pythagoras A^2 + B^2 = C^2:
            // 
            //    | _____\ |
            //    | C0.L1  |   =  r  (just the radius of the sphere)
            // 
            //                          .------------------------
            //    | _____\ |       /\  /   2     | _____\ | 2
            //    | C1.L1  |   =     \/   r   -  | C0.C1  |
            //                          
            // 
            // With that, we know the lengths needed:
            // 
            //    | _____\ |     | _____\ |     | _____\ |
            //    | P0.L1  |   = | P0.C1  |  -  | C1.L1  |
            // 
            //    | _____\ |     | _____\ |     | _____\ |
            //    | P0.L2  |   = | P0.C1  |  +  | C1.L1  |
            // 
            // Knowing those two lengths is really the point of the algorithm.  Getting
            // the actual points in question is just a matter of multiplying them by the
            // lookat unit vector, but that's left for the caller to do.
            
            double thetaRadians = Vector3d.Angle( rayVec, center - rayStart ) * 0.0174532925; // 0.0174532925 = Pi/180
            double lengthHypotenuse = (center - rayStart).magnitude;
            double lengthC0ToC1       = Math.Sin(thetaRadians) * lengthHypotenuse;
            if( debugMsg ) Debug.Log( "GetRayIntersectSphere: thetaRadians="+thetaRadians+" lengthHyp="+lengthHypotenuse+" lengthC0ToC1="+lengthC0ToC1);
            if (lengthC0ToC1 > radius)
            {
                nearDist = -1.0;
                farDist = -1.0;
                if( debugMsg ) Debug.Log( "GetRayIntersectSphere returning: "+false+", nearDist="+nearDist+", farDist="+farDist );
                return false;
            }
            double lengthP0ToC1 = Math.Cos(thetaRadians) * lengthHypotenuse;
            double lengthC1ToL1 = Math.Sqrt( Math.Pow(radius,2) - Math.Pow(lengthC0ToC1,2) );
            if( debugMsg ) Debug.Log( "GetRayIntersectSphere: legnthP0ToC1="+lengthP0ToC1+" lengthC1ToL1="+lengthC1ToL1);
            
            nearDist = lengthP0ToC1 - lengthC1ToL1;
            farDist = lengthP0ToC1 + lengthC1ToL1;
            if( debugMsg ) Debug.Log( "GetRayIntersectSphere returning: "+true+", nearDist="+nearDist+", farDist="+farDist );
            return true;
        }
    }
}
