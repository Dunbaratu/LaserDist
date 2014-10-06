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
        
        /// <summary>For measuring how long the current raycast solve chunk has been going.</summary>
        private System.Diagnostics.Stopwatch rayCastTimer;
        
        // These static settings tweak the numeric approximation algorithm:
        // ------------------------------------------------------------------
        
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
        
        /// <summary>The max amount of distance below sea level that it's reasonable to assume a hit might occur.
        /// Some bodies do have craters beneath the "sea" level - also there is continental shelf below the sea
        /// on Kerbin.  Note there are no places that are over 1000 meters below sea level, but the number is so high
        /// here because it might be being measured at a shallow angle.</summary>
        private double underSeaFudge = 5000;
        
        public LaserPQSUtil( Part p )
        {
            laserPart = p;
            millisecondCap = 1000.0 * (double)GameSettings.PHYSICS_FRAME_DT_LIMIT * tickPortionAllowed;
            PrevDist = 0;
            PrevSuccess = false;
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
            rayCastTimer = new System.Diagnostics.Stopwatch();
            rayCastTimer.Reset();
            rayCastTimer.Start();

            bool done = true;

            double seaLevelDist = -1.0;
            hitBody = null;
            dist = -1.0;

            // Start off with the sea level hit.
            bool didHit = raySeaLevelCast( origin, rayVec, out hitBody, out seaLevelDist );
            if( didHit )
            {
                dist = seaLevelDist;
            }
            // If there wasn't a sea level hit, then there might still be a terrain hit anyway, up above
            // the sea where the laser misses the sea level sphere but does hit the PQS terrain.  To deal
            // with that case, if there is no sea level hit then set a fake endpoint distance no farther
            // than the distance to the body's center.
            if( ! didHit )
            {
                hitBody = laserPart.vessel.GetOrbit().referenceBody;
                seaLevelDist = laserPart.vessel.altitude + hitBody.Radius;
            }
            
            // Only bother doing the expensive check with the PQS terrain when in the sphere of
            // influence of the hitBody - don't bother for hits on distance bodies - for them
            // the sea level hit is good enough:
            if( (! didHit) || laserPart.vessel.GetOrbit().referenceBody == hitBody )
            {
                // Begin the recursion for the more computationally expensive PQS numeric solver:
                // ------------------------------------------------------------------------------
                Vector3d pointingUnitVec = rayVec.normalized;
                double terrainDist;
                bool success = false;
                if( UpdateAge==0 ) // start a new instance of the algorithm from scratch.
                {
                    if( debugMsg ) Debug.Log( "Starting fresh new numericPQSolver");
                    success = numericPQSSolver(
                        out terrainDist, out done, hitBody, origin, pointingUnitVec, seaLevelDist+underSeaFudge, defaultSlices );
                }
                else // continue the previous one:
                {
                    if( debugMsg ) Debug.Log( "Starting continuation numericPQSolver");
                    success = numericPQSSolver(
                        out terrainDist, out done, this.hitBody, this.origin, this.pointingUnitVec, this.dist, defaultSlices );
                }
                if( success && done)
                {
                    didHit = true;
                    dist = terrainDist;
                }
            }
            rayCastTimer.Stop();
            
            if( done )
            {
                PrevDist = dist;
                PrevSuccess = didHit;
                PrevBodyName = hitBody.name;
                UpdateAge = 0;
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
        private bool raySeaLevelCast( Vector3d origin, Vector3d rayVec, out CelestialBody hitBody, out double dist )
        {
            List<CelestialBody> bodies = FlightGlobals.Bodies;
            double bestHitDist = -1.0;
            CelestialBody bestHitBody = null;
            double hitDist = -1.0;
            Vector3d hitVec;
            double now = Planetarium.GetUniversalTime();
            
            // For each body in the game, find if there's a hit with its surface calculator,
            // and if there is, then keep the hit that's closest (just in case two
            // bodies are in line of sight where a ray hits both of them, we want the
            // nearer of those hits.)
            foreach( CelestialBody body in bodies )
            {
                PQS pqs = body.pqsController;
                if( pqs != null )
                {
                    // This next line is needed because of what I believe to be a bug in
                    // KSP's PQS.RayIntersection method.  It appears to be rotating
                    // the input direction vector once the wrong way for its calculations,
                    // making it necessary to rotate it twice the correct way to compensate
                    // for the fact that it insists on rotating it the wrong way.  If a new
                    // release of KSP ever fixes this bug, then this next line will have to be
                    // edited.  That's why this long comment is here.  Please don't remove it.
                    Vector3d useRayVec = pqs.transformRotation * ( pqs.transformRotation * rayVec );

                    if( pqs.RayIntersection( origin, useRayVec, out hitVec ) )
                    {
                        Vector3d hitVecRelToOrigin = hitVec-origin;

                        // Check to see if the hit is "behind" the ray - because despite the name,
                        // pqs.RayIntersection actually finds hits anyhwere along the line, even
                        // behind the start of the ray.  If the hit is behind the ray, it doesn't
                        // count:
                        if( Vector3d.Angle( hitVecRelToOrigin, rayVec ) <= 90 )
                        {
                            hitDist = hitVecRelToOrigin.magnitude;
                            if( bestHitDist < 0 || bestHitDist > hitDist )
                            {
                                bestHitDist = hitDist;
                                bestHitBody = body;
                            }
                        }
                    }
                }
            }
            hitBody = bestHitBody;
            dist = bestHitDist;
            bool hitFound = (dist > 0);
            if( hitFound )
            {
                 // A hit has to be maintained steadily, long enough to to last a full lightspeed
                 // round trip, or it doesn't really count as a hit yet:
                if( now < lastFailTime + ((dist*2)/lightspeed) )
                {
                    hitFound = false;
                }
            }
            else
            {
                lastFailTime = now;
            }
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
        /// <returns>The closer dist that was hit, or -1 if no hit found</returns>
        private bool numericPQSSolver(
            out double newDist,
            out bool done,
            CelestialBody hitBody,
            Vector3d origin,
            Vector3d pointingUnitVec,
            double dist,
            int slices )
        {
            if( debugMsg ) Debug.Log( "numericPQSSolver( (out),(out),"+hitBody.name+", "+origin+", "+pointingUnitVec+", "+dist+", "+slices+");");
            bool success = false;
            bool continueNextTime = false;
            int i;
            double lat;
            double lng;
            double segmentLength = 0.0;
            newDist = dist;
            int slicesThisTime = slices;
            Vector3d samplePoint = origin;
            Vector3d prevSamplePoint;
            // We already know i=0 is above ground, so start at i=1:
            for( i = 1 ; i <= slicesThisTime ; ++i )
            {
                prevSamplePoint = samplePoint;
                samplePoint = origin + (i*(dist/slicesThisTime))*pointingUnitVec;
                segmentLength = (samplePoint - prevSamplePoint).magnitude;
                
                if( segmentLength <= epsilon )
                    break;

                lat = hitBody.GetLatitude( samplePoint );
                lng = hitBody.GetLongitude( samplePoint );

                var bodyUpVector = new Vector3d( 1, 0, 0 );
                bodyUpVector = QuaternionD.AngleAxis( lat, Vector3d.forward/*around Z axis*/ ) * bodyUpVector;
                bodyUpVector = QuaternionD.AngleAxis( lng, Vector3d.down/*around -Y axis*/ ) * bodyUpVector;

                double groundAlt = hitBody.pqsController.GetSurfaceHeight( bodyUpVector ) - hitBody.Radius;
                double samplePointAlt = hitBody.GetAltitude( samplePoint );
                
                if( samplePointAlt <= groundAlt )
                {
                    success = true;
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
    }
}
