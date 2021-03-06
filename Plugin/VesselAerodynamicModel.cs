﻿/*
Trajectories
Copyright 2014, Youen Toupin

This file is part of Trajectories, under MIT license.
*/

using ferram4;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Reflection;

namespace Trajectories
{
    // this class abstracts the game aerodynamic computations to provide an unified interface wether the stock drag is used, or a supported mod is installed
    class VesselAerodynamicModel
    {
        private double mass_;
        public double mass { get { return mass_; } }

        private Vessel vessel_;
        private double stockDragCoeff_;

        private Vector2[,] cachedFARForces; // cached aerodynamic forces in a two dimensional array : indexed by velocity magnitude, atmosphere density and angle of attack
        private double maxFARVelocity;
        private double maxFARAngleOfAttack; // valid values are in range [-maxFARAngleOfAttack ; maxFARAngleOfAttack]
        private bool isValid;
        private bool farInitialized = false;
        private bool useStockModel;
        private double referenceDrag = 0;
        private DateTime nextAllowedAutomaticUpdate = DateTime.Now;

        public VesselAerodynamicModel(Vessel vessel)
        {
            vessel_ = vessel;

            stockDragCoeff_ = 0.0;
            mass_ = 0.0;
            foreach (var part in vessel.Parts)
            {
                stockDragCoeff_ += part.maximum_drag * part.mass;
                mass_ += part.mass;
            }
            stockDragCoeff_ /= mass_;

            try
            {
                initFARModel();
            }
            catch (FileNotFoundException)
            {
                ScreenMessages.PostScreenMessage("Ferram Aerospace Research not installed, or incompatible version, using stock aerodynamics");
                ScreenMessages.PostScreenMessage("WARNING: stock aerodynamic model does not predict lift, spacecrafts with wings will have inaccurate predictions");
                useStockModel = true;
                isValid = true;
            }
        }

        public bool isValidFor(Vessel vessel)
        {
            if (vessel != vessel_)
                return false;

            if (!useStockModel && Settings.fetch.AutoUpdateAerodynamicModel)
            {
                double newRefDrag = computeFARReferenceDrag();
                if (referenceDrag == 0)
                    referenceDrag = newRefDrag;
                double ratio = Math.Max(newRefDrag, referenceDrag) / Math.Max(1, Math.Min(newRefDrag, referenceDrag));
                if (ratio > 1.2 && DateTime.Now > nextAllowedAutomaticUpdate)
                {
                    nextAllowedAutomaticUpdate = DateTime.Now.AddSeconds(10); // limit updates frequency (could make the game almost unresponsive on some computers)
                    ScreenMessages.PostScreenMessage("Trajectory aerodynamic model auto-updated");
                    isValid = false;
                }
            }

            return isValid;
        }

        public void IncrementalUpdate()
        {
            
        }

        public void Invalidate()
        {
            isValid = false;
        }

        public double computeFARReferenceDrag()
        {
            computeForces_FAR(10, 2, new Vector3d(3000.0, 0, 0), new Vector3(0, 1, 0), 0, 0.25);
            computeForces_FAR(10, 2, new Vector3d(3000.0, 0, 0), new Vector3(0, 1, 0), 0, 0.25);
            return computeForces_FAR(10, 2, new Vector3d(3000.0, 0, 0), new Vector3(0, 1, 0), 0, 0.25).sqrMagnitude;
        }

        private void initFARModel()
        {
            maxFARVelocity = 3000.0;
            maxFARAngleOfAttack = 45.0 / 180.0 * Math.PI;

            int velocityResolution = 128;
            int angleOfAttackResolution = 32;

            cachedFARForces = new Vector2[velocityResolution, angleOfAttackResolution];

            for (int v = 0; v < velocityResolution; ++v)
            {
                for (int a = 0; a < angleOfAttackResolution; ++a)
                {
                    cachedFARForces[v, a] = new Vector2(float.NaN, float.NaN);
                }
            }

            isValid = true;
        }

        private Vector2 getCachedFARForce(int v, int a)
        {
            //if (v < 0 || v >= cachedFARForces.GetLength(0))
            //    Util.PostSingleScreenMessage("v out of range", "Out of range: v = " + v);
            //if (a < 0 || a >= cachedFARForces.GetLength(1))
            //    Util.PostSingleScreenMessage("a out of range", "Out of range: a = " + a);
            Vector2 f = cachedFARForces[v,a];

            double vel = maxFARVelocity * (double)v / (double)(cachedFARForces.GetLength(0) - 1);
            double v2 = Math.Max(1.0, vel * vel);

            if(float.IsNaN(f.x))
            {
                Vector3d velocity = new Vector3d(vel, 0, 0);
                double machNumber = velocity.magnitude / 300.0; // sound speed approximation
                double AoA = maxFARAngleOfAttack * ((double)a / (double)(cachedFARForces.GetLength(1) - 1) * 2.0 - 1.0);
                Vector3d force = computeForces_FAR(1.0, machNumber, velocity, new Vector3(0,1,0), AoA, 0.25);
                f = new Vector2((float)(force.x/v2), (float)(force.y/v2)); // divide by v² before storing the force, to increase accuracy (the reverse operation is performed when reading from the cache)

                bool validForce = farInitialized;
                if(!validForce)
                {
                    // double check if FAR is correctly initialized before caching the value
                    if (computeFARReferenceDrag() >= 1)
                    {
                        validForce = true;
                        farInitialized = true;
                    }
                }

                if (validForce)
                {
                    cachedFARForces[v, a] = f;
                }
            }

            return f * (float)v2;
        }

        private Vector2 getFARForce(double velocity, double rho, double angleOfAttack)
        {
            //Util.PostSingleScreenMessage("getFARForce velocity", "velocity = " + velocity);
            float vFrac = (float)(velocity / maxFARVelocity * (double)(cachedFARForces.GetLength(0)-1));
            int vFloor = Math.Min(cachedFARForces.GetLength(0)-2, (int)vFrac);
            vFrac = Math.Min(1.0f, vFrac - (float)vFloor);

            float aFrac = (float)((angleOfAttack / maxFARAngleOfAttack * 0.5 + 0.5) * (double)(cachedFARForces.GetLength(1) - 1));
            int aFloor = Math.Max(0, Math.Min(cachedFARForces.GetLength(1) - 2, (int)aFrac));
            aFrac = Math.Max(0.0f, Math.Min(1.0f, aFrac - (float)aFloor));

            Vector2 f00 = getCachedFARForce(vFloor, aFloor);
            Vector2 f10 = getCachedFARForce(vFloor + 1, aFloor);

            Vector2 f01 = getCachedFARForce(vFloor, aFloor + 1);
            Vector2 f11 = getCachedFARForce(vFloor + 1, aFloor + 1);

            Vector2 f0 = f00 * aFrac + f01 * (1.0f - aFrac);
            Vector2 f1 = f10 * aFrac + f11 * (1.0f - aFrac);

            Vector2 res = f1 * vFrac + f0 * (1.0f - vFrac);
            res = res * (float)rho;

            return res;
        }

        // returns the total aerodynamic forces that would be applied on the vessel if it was at bodySpacePosition with bodySpaceVelocity relatively to the specified celestial body
        // dt is the time delta during which the force will be applied, so if the model supports it, it can compute an average force (to be more accurate than a simple instantaneous force)
        public Vector3d computeForces(CelestialBody body, Vector3d bodySpacePosition, Vector3d airVelocity, double angleOfAttack, double dt)
        {
            if(useStockModel)
                return computeForces_StockDrag(body, bodySpacePosition, airVelocity, dt); // TODO: compute stock lift
            else
                return computeForces_FAR(body, bodySpacePosition, airVelocity, angleOfAttack, dt);
        }

        private Vector3d computeForces_StockDrag(CelestialBody body, Vector3d bodySpacePosition, Vector3d airVelocity, double dt)
        {
            double altitudeAboveSea = bodySpacePosition.magnitude - body.Radius;
            double pressure = FlightGlobals.getStaticPressure(altitudeAboveSea, body);
            if (pressure <= 0)
                return Vector3d.zero;

            double rho = FlightGlobals.getAtmDensity(pressure);

            double velocityMag = airVelocity.magnitude;

            double crossSectionalArea = FlightGlobals.DragMultiplier * mass_;
            return airVelocity * (-0.5 * rho * velocityMag * stockDragCoeff_ * crossSectionalArea);
        }

        public Vector3d computeForces_FAR(double rho, double machNumber, Vector3d airVelocity, Vector3d vup, double angleOfAttack, double dt)
        {
            Transform vesselTransform = vessel_.ReferenceTransform;

            // this is weird, but the vessel orientation does not match the reference transform (up is forward), this code fixes it but I don't know if it'll work in all cases
            Vector3d vesselBackward = (Vector3d)(-vesselTransform.up.normalized);
            Vector3d vesselForward = -vesselBackward;
            Vector3d vesselUp = (Vector3d)(-vesselTransform.forward.normalized);
            Vector3d vesselRight = Vector3d.Cross(vesselUp, vesselBackward).normalized;

            Vector3d airVelocityForFixedAoA = (vesselForward * Math.Cos(-angleOfAttack) + vesselUp * Math.Sin(-angleOfAttack)) * airVelocity.magnitude;

            Vector3d totalForce = new Vector3d(0, 0, 0);

            foreach (var part in vessel_.Parts)
            {
                if (part.Rigidbody == null)
                    continue;

                var dragModel = part.FindModuleImplementing<FARBasicDragModel>();
                if (dragModel != null)
                {
                    // make sure we don't trigger aerodynamic failures during prediction
                    double YmaxForce = dragModel.YmaxForce;
                    double XZmaxForce = dragModel.XZmaxForce;
                    dragModel.YmaxForce = Double.MaxValue;
                    dragModel.XZmaxForce = Double.MaxValue;

                    totalForce += dragModel.RunDragCalculation(airVelocityForFixedAoA, machNumber, rho);


                    dragModel.YmaxForce = YmaxForce;
                    dragModel.XZmaxForce = XZmaxForce;
                }

                var wingModel = part.FindModuleImplementing<FARWingAerodynamicModel>();
                if (wingModel != null)
                {
                    // make sure we don't trigger aerodynamic failures during prediction
                    double YmaxForce = wingModel.YmaxForce;
                    double XZmaxForce = wingModel.XZmaxForce;
                    wingModel.YmaxForce = Double.MaxValue;
                    wingModel.XZmaxForce = Double.MaxValue;

                    // here we must use reflexion to set rho in the wing model, as there is no public access to it
                    var rhoField = typeof(FARWingAerodynamicModel).GetField("rho", BindingFlags.NonPublic | BindingFlags.Instance);
                    double rhoBackup = (double)rhoField.GetValue(wingModel);
                    rhoField.SetValue(wingModel, rho);

                    // FAR uses the stall value computed in the previous frame to compute the new one. This is incompatible with prediction code that shares the same state variables as the normal simulation.
                    // This is also incompatible with forces caching that is made to improve performances, as such caching can't depend on the previous wing state
                    // To solve this problem, we assume wings never stall during prediction, and we backup/restore the stall value each time
                    var stallField = typeof(FARWingAerodynamicModel).GetField("stall", BindingFlags.NonPublic | BindingFlags.Instance);
                    double stallBackup = (double)stallField.GetValue(wingModel);
                    stallField.SetValue(wingModel, 0);

                    double PerpVelocity = Vector3d.Dot(wingModel.part.partTransform.forward, airVelocityForFixedAoA.normalized);
                    double FARAoA = Math.Asin(FARMathUtil.Clamp(PerpVelocity, -1, 1));
                    totalForce += wingModel.CalculateForces(airVelocityForFixedAoA, machNumber, FARAoA);

                    rhoField.SetValue(wingModel, rhoBackup);
                    stallField.SetValue(wingModel, stallBackup);

                    wingModel.YmaxForce = YmaxForce;
                    wingModel.XZmaxForce = XZmaxForce;
                }
            }

            //if (Double.IsNaN(totalForce.x) || Double.IsNaN(totalForce.y) || Double.IsNaN(totalForce.z))
            //    throw new Exception("totalForce is NAN");

            // convert the force computed by FAR (depends on the current vessel orientation, which is irrelevant for the prediction) to the predicted vessel orientation (which depends on the predicted velocity)
            Vector3d localForce = new Vector3d(Vector3d.Dot(vesselRight, totalForce), Vector3d.Dot(vesselUp, totalForce), Vector3d.Dot(vesselBackward, totalForce));

            //if (Double.IsNaN(localForce.x) || Double.IsNaN(localForce.y) || Double.IsNaN(localForce.z))
            //    throw new Exception("localForce is NAN");

            Vector3d velForward = airVelocity.normalized;
            Vector3d velBackward = -velForward;
            Vector3d velRight = Vector3d.Cross(vup, velBackward);
            if (velRight.sqrMagnitude < 0.001)
            {
                velRight = Vector3d.Cross(vesselUp, velBackward);
                if (velRight.sqrMagnitude < 0.001)
                {
                    velRight = Vector3d.Cross(vesselBackward, velBackward).normalized;
                }
                else
                {
                    velRight = velRight.normalized;
                }
            }
            else
                velRight = velRight.normalized;
            Vector3d velUp = Vector3d.Cross(velBackward, velRight).normalized;

            Vector3d predictedVesselForward = velForward * Math.Cos(angleOfAttack) + velUp * Math.Sin(angleOfAttack);
            Vector3d predictedVesselBackward = -predictedVesselForward;
            Vector3d predictedVesselRight = velRight;
            Vector3d predictedVesselUp = Vector3d.Cross(predictedVesselBackward, predictedVesselRight).normalized;

            Vector3d res = predictedVesselRight * localForce.x + predictedVesselUp * localForce.y + predictedVesselBackward * localForce.z;
            //if (Double.IsNaN(res.x) || Double.IsNaN(res.y) || Double.IsNaN(res.z))
            //    throw new Exception("res is NAN");
            return res;
        }

        private Vector3d computeForces_FAR(CelestialBody body, Vector3d bodySpacePosition, Vector3d airVelocity, double angleOfAttack, double dt)
        {
            double altitudeAboveSea = bodySpacePosition.magnitude - body.Radius;

            double rho = FARAeroUtil.GetCurrentDensity(body, altitudeAboveSea);

            double machNumber = FARAeroUtil.GetMachNumber(body, altitudeAboveSea, airVelocity);

            // uncomment the next line to bypass the cache system (for debugging, in case you suspect a bug or inaccuracy related to the cache system)
            //return computeForces_FAR(rho, machNumber, airVelocity, bodySpacePosition, angleOfAttack, dt);

            //Util.PostSingleScreenMessage("airVelocity", "airVelocity = " + airVelocity);
            Vector2 force = getFARForce(airVelocity.magnitude, rho, angleOfAttack);
            //if (float.IsNaN(force.x) || float.IsNaN(force.y))
            //{
            //    throw new Exception("force is NAN: bodySpacePosition = " + bodySpacePosition + ", airVelocity=" + airVelocity + ", angleOfAttack=" + angleOfAttack + ", dt=" + dt);
            //}

            Vector3d forward = airVelocity.normalized;
            Vector3d right = Vector3d.Cross(forward, bodySpacePosition).normalized;
            Vector3d up = Vector3d.Cross(right, forward).normalized;

            return forward * force.x + up * force.y;
        }
    }
}
