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
        // These static settings tweak the numeric approximation algorithm:
        // ------------------------------------------------------------------
        
        // Number of meters of error that's considered "good enough" to stop the algorithm:
        // Because this is only used for terrain that's very far away, and also the PQS
        // predicted terrain won't exactly match the actual terrain polygons that it gets
        // modelled with when you get closer, making it more accurate is misleading anyway.
        private static double epsilon = 3;
        
        // Number of maximum iterations allowed before aborting the algorithm and accepting
        // the answer (even if it's not as accurate as epsilon yet).  (On a 2.4 ghz cpu this
        // was tested on, you can get about 75 iterations per 1 millisecond).  This could probably
        // go a few more iterations than it does, but I want to be polite to other mods that are
        // trying to operate in the same Update and keep my execution footprint as quick as possible.
        private static int iterationCap = 100;
        
        // Number of slices to typically divide the line segment into per recursion level,
        // although some circumstances can change this:
        private static int defaultSlices = 30;

        private static double lightspeed = 299792458; // in m/s

        private double lastFailTime = 0.0;
        private Part laserPart;

        
        public LaserPQSUtil( Part p )
        {
            laserPart = p;
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
            double seaLevelDist = -1.0;
            hitBody = null;
            dist = -1.0;

            // Start off with the sea level hit, knowing the terrain hit has to be no farther
            // than that:
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
                bool success = numericPQSSolver(
                    out terrainDist, hitBody, origin, pointingUnitVec, iterationCap, seaLevelDist, defaultSlices );
                if( success )
                {
                    didHit = true;
                    dist = terrainDist;
                }
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
        /// <param name="hitBody">body of the terrain</param>
        /// <param name="origin">start of this line segment of the ray</param>
        /// <param name="pointingUnitVec">direction of the ray - must be a unit vector starting at origin</param>
        /// <param name="itersLeft">Number of iterations allowed before exiting and accepting the answer so far</param>
        /// <param name="dist">length of this line segment of the ray</param>
        /// <param name="slices">Number of slices to try to cut the line segment into</param>
        /// <returns>The closer dist that was hit, or -1 if no hit found</returns>
        private bool numericPQSSolver(
            out double newDist,
            CelestialBody hitBody,
            Vector3d origin,
            Vector3d pointingUnitVec,
            int itersLeft,
            double dist,
            int slices )
        {
            Debug.Log( "numericPQSSolver( "+hitBody.name+", "+origin+", "+pointingUnitVec+", "+itersLeft+", "+dist+", "+slices+");");
            bool success = false;
            int i;
            double lat;
            double lng;
            newDist = dist;
            int slicesThisTime = Math.Min( itersLeft, slices );
            Vector3d samplePoint = origin;
            Vector3d prevSamplePoint;
            // We already know i=0 is above ground, so start at i=1:
            for( i = 1 ; i <= slicesThisTime ; ++i )
            {
                prevSamplePoint = samplePoint;
                samplePoint = origin + (i*(dist/slicesThisTime))*pointingUnitVec;
                double segmentLength = (samplePoint - prevSamplePoint).magnitude;
                
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
                    numericPQSSolver( out subSectionDist, hitBody, prevSamplePoint, pointingUnitVec,
                                      itersLeft - i, segmentLength, slices );
                    newDist = ((i-1)*(dist/slicesThisTime)) + subSectionDist;
                    break;
                }
            }
            Debug.Log( "numericPQSSolver reached i = "+ i );
            
            if( i > slicesThisTime )
            {
                // The above loop got to the end without finding a hit.
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
                //
                // Therefore if there's enough itersLeft remaining to try again with a tighter
                // sampling, then do so:
                if( itersLeft-i >= slices )
                {
                    success = numericPQSSolver( out newDist, hitBody, origin, pointingUnitVec,
                                                itersLeft - slicesThisTime, dist, 2*slices );
                }
            }
            Debug.Log( "numericPQSSolver returning "+ success+ " dist="+newDist );
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
