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

namespace LaserDist
{
    /// <summary>
    /// The PQS calculating tool to go with this laser distometer.
    /// Create a new instance of this per laser dist meter.
    /// </summary>
    public class LaserPQSUtil
    {
        double lightspeed = 299792458; // in m/s
        double lastFailTime = 0.0;

        public bool RayCast( Vector3d origin, Vector3d rayVec, out string name, out double dist )
        {
            List<CelestialBody> bodies = FlightGlobals.Bodies;
            double bestHitDist = -1.0;
            string bestHitName = "<none>";
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
                                bestHitName = body.GetName();
                            }
                        }
                    }
                }
            }
            name = bestHitName;
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
    }
}
