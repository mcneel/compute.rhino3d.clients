using System;
using System.IO;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Rhino.Collections;
using System.Runtime.Serialization;

namespace Rhino.Compute
{
    public static class ComputeServer
    {
        public static string WebAddress { get; set; } = "https://compute.rhino3d.com";
        public static string AuthToken { get; set; }
        public static string ApiKey { get; set; }
        public static string Version => "0.12.2";

        public static T Post<T>(string function, params object[] postData)
        {
            return PostWithConverter<T>(function, null, postData);
        }

        public static T PostWithConverter<T>(string function, JsonConverter converter, params object[] postData)
        {
            if (string.IsNullOrWhiteSpace(AuthToken) && WebAddress.Equals("https://compute.rhino3d.com"))
                throw new UnauthorizedAccessException("AuthToken must be set for compute.rhino3d.com");

            for( int i=0; i<postData.Length; i++ )
            {
                if( postData[i]!=null &&
                    postData[i].GetType().IsGenericType &&
                    postData[i].GetType().GetGenericTypeDefinition() == typeof(Remote<>) )
                {
                    var mi = postData[i].GetType().GetMethod("JsonObject");
                    postData[i] = mi.Invoke(postData[i], null);
                }
            }

            string json = converter == null ?
                JsonConvert.SerializeObject(postData, Formatting.None) :
                JsonConvert.SerializeObject(postData, Formatting.None, converter);
            var response = DoPost(function, json);
            using (var streamReader = new StreamReader(response.GetResponseStream()))
            {
                var result = streamReader.ReadToEnd();
                if (converter == null)
                    return JsonConvert.DeserializeObject<T>(result);
                return JsonConvert.DeserializeObject<T>(result, converter);
            }
        }

        public static T0 Post<T0, T1>(string function, out T1 out1, params object[] postData)
        {
            if (string.IsNullOrWhiteSpace(AuthToken) && WebAddress.Equals("https://compute.rhino3d.com"))
                throw new UnauthorizedAccessException("AuthToken must be set for compute.rhino3d.com");

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(postData);
            var response = DoPost(function, json);
            using (var streamReader = new StreamReader(response.GetResponseStream()))
            {
                var jsonString = streamReader.ReadToEnd();
                object data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonString);
                var ja = data as Newtonsoft.Json.Linq.JArray;
                out1 = ja[1].ToObject<T1>();
                return ja[0].ToObject<T0>();
            }
        }

        public static T0 Post<T0, T1, T2>(string function, out T1 out1, out T2 out2, params object[] postData)
        {
            if (string.IsNullOrWhiteSpace(AuthToken) && WebAddress.Equals("https://compute.rhino3d.com"))
                throw new UnauthorizedAccessException("AuthToken must be set for compute.rhino3d.com");

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(postData);
            var response = DoPost(function, json);
            using (var streamReader = new StreamReader(response.GetResponseStream()))
            {
                var jsonString = streamReader.ReadToEnd();
                object data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonString);
                var ja = data as Newtonsoft.Json.Linq.JArray;
                out1 = ja[1].ToObject<T1>();
                out2 = ja[2].ToObject<T2>();
                return ja[0].ToObject<T0>();
            }
        }

        // run all requests through here
        private static System.Net.WebResponse DoPost(string function, string json)
        {

            if (!function.StartsWith("/")) // add leading /
                function = "/" + function; // if not present

            string uri = $"{WebAddress}{function}".ToLower();
            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(uri);
            request.ContentType = "application/json";
            request.UserAgent = $"compute.rhino3d.cs/{Version}";
            request.Method = "POST";

            // try auth token (compute.rhino3d.com only)
            if (!string.IsNullOrWhiteSpace(AuthToken))
                request.Headers.Add("Authorization", "Bearer " + AuthToken);

            // try api key (self-hosted compute)
            if (!string.IsNullOrWhiteSpace(ApiKey))
                request.Headers.Add("RhinoComputeKey", ApiKey);
            
            using (var streamWriter = new StreamWriter(request.GetRequestStream()))
            {
                streamWriter.Write(json);
                streamWriter.Flush();
            }

            return request.GetResponse();
        }

        public static string ApiAddress(Type t, string function)
        {
            string s = t.ToString().Replace('.', '/');
            return s + "/" + function;
        }
    }

    public class Remote<T>
    {
        string _url;
        T _data;

        public Remote(string url)
        {
            _url = url;
        }

        public Remote(T data)
        {
            _data = data;
        }

        public object JsonObject()
        {
            if( _url!=null )
            {
                Dictionary<string, string> dict = new Dictionary<string, string>();
                dict["url"] = _url;
                return dict;
            }
            return _data;
        }
    }


    public static class PythonCompute
    {
        // NOTE: If you are using a Rhino 6 based version of Rhino3dmIO, you will
        // get compile errors due to ArchivableDictionary not implementing ISerializble in V6
        //
        // Either update to a V7 version of Rhino3dmIO (check the prerelease box on nuget)
        // -or-
        // Delete the entire PythonCompute class here. Python functionality is only available
        // using a V7 based Rhino3dmIO assembly
        class ArchivableDictionaryResolver : JsonConverter
        {
            public override bool CanConvert(Type objectType) { return objectType == typeof(ArchivableDictionary); }
            public override bool CanRead => true;
            public override bool CanWrite => true;

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                string encoded = (string)reader.Value;
                var dh = JsonConvert.DeserializeObject<DictHelper>(encoded);
                return dh.SerializedDictionary;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                string json = JsonConvert.SerializeObject(new DictHelper((ArchivableDictionary)value));
                writer.WriteValue(json);
            }


            [Serializable]
            class DictHelper : ISerializable
            {
                public ArchivableDictionary SerializedDictionary { get; set; }
                public DictHelper(ArchivableDictionary d) { SerializedDictionary = d; }
                public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
                {
                    SerializedDictionary.GetObjectData(info, context);
                }
                protected DictHelper(SerializationInfo info, StreamingContext context)
                {
                    Type t = typeof(ArchivableDictionary);
                    var constructor = t.GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                      null, new Type[] { typeof(SerializationInfo), typeof(StreamingContext) }, null);
                    SerializedDictionary = constructor.Invoke(new object[] { info, context }) as ArchivableDictionary;
                }
            }
        }


        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return "rhino/python/" + caller;
        }

        public static ArchivableDictionary Evaluate(string script, ArchivableDictionary input)
        {
            return ComputeServer.PostWithConverter<ArchivableDictionary>(ApiAddress(), new ArchivableDictionaryResolver(), script, input);
        }
    }

    public static class NurbsCurveCompute
    {
        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return ComputeServer.ApiAddress(typeof(NurbsCurve), caller);
        }

        /// <summary>
        /// For expert use only. From the input curves, make an array of compatible NURBS curves.
        /// </summary>
        /// <param name="curves">The input curves.</param>
        /// <param name="startPt">The start point. To omit, specify Point3d.Unset.</param>
        /// <param name="endPt">The end point. To omit, specify Point3d.Unset.</param>
        /// <param name="simplifyMethod">The simplify method.</param>
        /// <param name="numPoints">The number of rebuild points.</param>
        /// <param name="refitTolerance">The refit tolerance.</param>
        /// <param name="angleTolerance">The angle tolerance in radians.</param>
        /// <returns>The output NURBS surfaces if successful.</returns>
        public static NurbsCurve[] MakeCompatible(IEnumerable<Curve> curves, Point3d startPt, Point3d endPt, int simplifyMethod, int numPoints, double refitTolerance, double angleTolerance)
        {
            return ComputeServer.Post<NurbsCurve[]>(ApiAddress(), curves, startPt, endPt, simplifyMethod, numPoints, refitTolerance, angleTolerance);
        }

        /// <summary>
        /// For expert use only. From the input curves, make an array of compatible NURBS curves.
        /// </summary>
        /// <param name="curves">The input curves.</param>
        /// <param name="startPt">The start point. To omit, specify Point3d.Unset.</param>
        /// <param name="endPt">The end point. To omit, specify Point3d.Unset.</param>
        /// <param name="simplifyMethod">The simplify method.</param>
        /// <param name="numPoints">The number of rebuild points.</param>
        /// <param name="refitTolerance">The refit tolerance.</param>
        /// <param name="angleTolerance">The angle tolerance in radians.</param>
        /// <returns>The output NURBS surfaces if successful.</returns>
        public static NurbsCurve[] MakeCompatible(Remote<IEnumerable<Curve>> curves, Point3d startPt, Point3d endPt, int simplifyMethod, int numPoints, double refitTolerance, double angleTolerance)
        {
            return ComputeServer.Post<NurbsCurve[]>(ApiAddress(), curves, startPt, endPt, simplifyMethod, numPoints, refitTolerance, angleTolerance);
        }

        /// <summary>
        /// Creates a parabola from vertex and end points.
        /// </summary>
        /// <param name="vertex">The vertex point.</param>
        /// <param name="startPoint">The start point.</param>
        /// <param name="endPoint">The end point</param>
        /// <returns>A 2 degree NURBS curve if successful, false otherwise.</returns>
        public static NurbsCurve CreateParabolaFromVertex(Point3d vertex, Point3d startPoint, Point3d endPoint)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), vertex, startPoint, endPoint);
        }

        /// <summary>
        /// Creates a parabola from focus and end points.
        /// </summary>
        /// <param name="focus">The focal point.</param>
        /// <param name="startPoint">The start point.</param>
        /// <param name="endPoint">The end point</param>
        /// <returns>A 2 degree NURBS curve if successful, false otherwise.</returns>
        public static NurbsCurve CreateParabolaFromFocus(Point3d focus, Point3d startPoint, Point3d endPoint)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), focus, startPoint, endPoint);
        }

        /// <summary>
        /// Create a uniform non-rational cubic NURBS approximation of an arc.
        /// </summary>
        /// <param name="arc"></param>
        /// <param name="degree">&gt;=1</param>
        /// <param name="cvCount">CV count &gt;=5</param>
        /// <returns>NURBS curve approximation of an arc on success</returns>
        public static NurbsCurve CreateFromArc(Arc arc, int degree, int cvCount)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), arc, degree, cvCount);
        }

        /// <summary>
        /// Construct an H-spline from a sequence of interpolation points
        /// </summary>
        /// <param name="points">Points to interpolate</param>
        /// <returns></returns>
        public static NurbsCurve CreateHSpline(IEnumerable<Point3d> points)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), points);
        }

        /// <summary>
        /// Construct an H-spline from a sequence of interpolation points and
        /// optional start and end derivative information
        /// </summary>
        /// <param name="points">Points to interpolate</param>
        /// <param name="startTangent">Unit tangent vector or Unset</param>
        /// <param name="endTangent">Unit tangent vector or Unset</param>
        /// <returns>NURBS curve approximation of an arc on success</returns>
        public static NurbsCurve CreateHSpline(IEnumerable<Point3d> points, Vector3d startTangent, Vector3d endTangent)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), points, startTangent, endTangent);
        }

        /// <summary>
        /// Create a NURBS curve, that is suitable for calculations like lofting SubD objects, through a sequence of curves.
        /// </summary>
        /// <param name="points">
        /// An enumeration of points. Adjacent points must not be equal.
        /// If periodicClosedCurve is false, there must be at least two points.
        /// If periodicClosedCurve is true, there must be at least three points and it is not necessary to duplicate the first and last points.
        /// When periodicClosedCurve is true and the first and last points are equal, the duplicate last point is automatically ignored.
        /// </param>
        /// <param name="interpolatePoints">
        /// True if the curve should interpolate the points. False if points specify control point locations.
        /// In either case, the curve will begin at the first point and end at the last point.
        /// </param>
        /// <param name="periodicClosedCurve">
        /// True to create a periodic closed curve. Do not duplicate the start/end point in the point input.
        /// </param>
        /// <returns>A SubD friendly NURBS curve is successful, null otherwise.</returns>
        public static NurbsCurve CreateSubDFriendly(IEnumerable<Point3d> points, bool interpolatePoints, bool periodicClosedCurve)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), points, interpolatePoints, periodicClosedCurve);
        }

        /// <summary>
        /// Create a NURBS curve, that is suitable for calculations like lofting SubD objects, from an existing curve.
        /// </summary>
        /// <param name="curve">Curve to rebuild as a SubD friendly curve.</param>
        /// <returns>A SubD friendly NURBS curve is successful, null otherwise.</returns>
        public static NurbsCurve CreateSubDFriendly(Curve curve)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), curve);
        }

        /// <summary>
        /// Create a NURBS curve, that is suitable for calculations like lofting SubD objects, from an existing curve.
        /// </summary>
        /// <param name="curve">Curve to rebuild as a SubD friendly curve.</param>
        /// <returns>A SubD friendly NURBS curve is successful, null otherwise.</returns>
        public static NurbsCurve CreateSubDFriendly(Remote<Curve> curve)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), curve);
        }

        /// <summary>
        /// Create a NURBS curve, that is suitable for calculations like lofting SubD objects, from an existing curve.
        /// </summary>
        /// <param name="curve">Curve to rebuild as a SubD friendly curve.</param>
        /// <param name="pointCount">
        /// Desired number of control points. If periodicClosedCurve is true, the number must be &gt;= 6, otherwise the number must be &gt;= 4.
        /// </param>
        /// <param name="periodicClosedCurve">True if the SubD friendly curve should be closed and periodic. False in all other cases.</param>
        /// <returns>A SubD friendly NURBS curve is successful, null otherwise.</returns>
        public static NurbsCurve CreateSubDFriendly(Curve curve, int pointCount, bool periodicClosedCurve)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), curve, pointCount, periodicClosedCurve);
        }

        /// <summary>
        /// Create a NURBS curve, that is suitable for calculations like lofting SubD objects, from an existing curve.
        /// </summary>
        /// <param name="curve">Curve to rebuild as a SubD friendly curve.</param>
        /// <param name="pointCount">
        /// Desired number of control points. If periodicClosedCurve is true, the number must be &gt;= 6, otherwise the number must be &gt;= 4.
        /// </param>
        /// <param name="periodicClosedCurve">True if the SubD friendly curve should be closed and periodic. False in all other cases.</param>
        /// <returns>A SubD friendly NURBS curve is successful, null otherwise.</returns>
        public static NurbsCurve CreateSubDFriendly(Remote<Curve> curve, int pointCount, bool periodicClosedCurve)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), curve, pointCount, periodicClosedCurve);
        }

        /// <summary>
        /// Computes planar rail sweep frames at specified parameters.
        /// </summary>
        /// <param name="parameters">A collection of curve parameters.</param>
        /// <param name="normal">Unit normal to the plane.</param>
        /// <returns>An array of planes if successful, or an empty array on failure.</returns>
        public static Plane[] CreatePlanarRailFrames(this NurbsCurve nurbscurve, out NurbsCurve updatedInstance, IEnumerable<double> parameters, Vector3d normal)
        {
            return ComputeServer.Post<Plane[], NurbsCurve>(ApiAddress(), out updatedInstance, nurbscurve, parameters, normal);
        }

        /// <summary>
        /// Computes planar rail sweep frames at specified parameters.
        /// </summary>
        /// <param name="parameters">A collection of curve parameters.</param>
        /// <param name="normal">Unit normal to the plane.</param>
        /// <returns>An array of planes if successful, or an empty array on failure.</returns>
        public static Plane[] CreatePlanarRailFrames(Remote<NurbsCurve> nurbscurve, out NurbsCurve updatedInstance, IEnumerable<double> parameters, Vector3d normal)
        {
            return ComputeServer.Post<Plane[], NurbsCurve>(ApiAddress(), out updatedInstance, nurbscurve, parameters, normal);
        }

        /// <summary>
        /// Computes relatively parallel rail sweep frames at specified parameters.
        /// </summary>
        /// <param name="parameters">A collection of curve parameters.</param>
        /// <returns>An array of planes if successful, or an empty array on failure.</returns>
        public static Plane[] CreateRailFrames(this NurbsCurve nurbscurve, out NurbsCurve updatedInstance, IEnumerable<double> parameters)
        {
            return ComputeServer.Post<Plane[], NurbsCurve>(ApiAddress(), out updatedInstance, nurbscurve, parameters);
        }

        /// <summary>
        /// Computes relatively parallel rail sweep frames at specified parameters.
        /// </summary>
        /// <param name="parameters">A collection of curve parameters.</param>
        /// <returns>An array of planes if successful, or an empty array on failure.</returns>
        public static Plane[] CreateRailFrames(Remote<NurbsCurve> nurbscurve, out NurbsCurve updatedInstance, IEnumerable<double> parameters)
        {
            return ComputeServer.Post<Plane[], NurbsCurve>(ApiAddress(), out updatedInstance, nurbscurve, parameters);
        }

        /// <summary>
        /// Create a uniform non-rational cubic NURBS approximation of a circle.
        /// </summary>
        /// <param name="circle"></param>
        /// <param name="degree">&gt;=1</param>
        /// <param name="cvCount">CV count &gt;=5</param>
        /// <returns>NURBS curve approximation of a circle on success</returns>
        public static NurbsCurve CreateFromCircle(Circle circle, int degree, int cvCount)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), circle, degree, cvCount);
        }

        /// <summary>
        /// Set end condition of a NURBS curve to point, tangent and curvature.
        /// </summary>
        /// <param name="bSetEnd">true: set end of curve, false: set start of curve </param>
        /// <param name="continuity">Position: set start or end point, Tangency: set point and tangent, Curvature: set point, tangent and curvature </param>
        /// <param name="point">point to set </param>
        /// <param name="tangent">tangent to set</param>
        /// <returns>true on success, false on failure.</returns>
        public static bool SetEndCondition(this NurbsCurve nurbscurve, out NurbsCurve updatedInstance, bool bSetEnd, NurbsCurveEndConditionType continuity, Point3d point, Vector3d tangent)
        {
            return ComputeServer.Post<bool, NurbsCurve>(ApiAddress(), out updatedInstance, nurbscurve, bSetEnd, continuity, point, tangent);
        }

        /// <summary>
        /// Set end condition of a NURBS curve to point, tangent and curvature.
        /// </summary>
        /// <param name="bSetEnd">true: set end of curve, false: set start of curve </param>
        /// <param name="continuity">Position: set start or end point, Tangency: set point and tangent, Curvature: set point, tangent and curvature </param>
        /// <param name="point">point to set </param>
        /// <param name="tangent">tangent to set</param>
        /// <returns>true on success, false on failure.</returns>
        public static bool SetEndCondition(Remote<NurbsCurve> nurbscurve, out NurbsCurve updatedInstance, bool bSetEnd, NurbsCurveEndConditionType continuity, Point3d point, Vector3d tangent)
        {
            return ComputeServer.Post<bool, NurbsCurve>(ApiAddress(), out updatedInstance, nurbscurve, bSetEnd, continuity, point, tangent);
        }

        /// <summary>
        /// Set end condition of a NURBS curve to point, tangent and curvature.
        /// </summary>
        /// <param name="bSetEnd">true: set end of curve, false: set start of curve </param>
        /// <param name="continuity">Position: set start or end point, Tangency: set point and tangent, Curvature: set point, tangent and curvature </param>
        /// <param name="point">point to set </param>
        /// <param name="tangent">tangent to set</param>
        /// <param name="curvature">curvature to set</param>
        /// <returns>true on success, false on failure.</returns>
        public static bool SetEndCondition(this NurbsCurve nurbscurve, out NurbsCurve updatedInstance, bool bSetEnd, NurbsCurveEndConditionType continuity, Point3d point, Vector3d tangent, Vector3d curvature)
        {
            return ComputeServer.Post<bool, NurbsCurve>(ApiAddress(), out updatedInstance, nurbscurve, bSetEnd, continuity, point, tangent, curvature);
        }

        /// <summary>
        /// Set end condition of a NURBS curve to point, tangent and curvature.
        /// </summary>
        /// <param name="bSetEnd">true: set end of curve, false: set start of curve </param>
        /// <param name="continuity">Position: set start or end point, Tangency: set point and tangent, Curvature: set point, tangent and curvature </param>
        /// <param name="point">point to set </param>
        /// <param name="tangent">tangent to set</param>
        /// <param name="curvature">curvature to set</param>
        /// <returns>true on success, false on failure.</returns>
        public static bool SetEndCondition(Remote<NurbsCurve> nurbscurve, out NurbsCurve updatedInstance, bool bSetEnd, NurbsCurveEndConditionType continuity, Point3d point, Vector3d tangent, Vector3d curvature)
        {
            return ComputeServer.Post<bool, NurbsCurve>(ApiAddress(), out updatedInstance, nurbscurve, bSetEnd, continuity, point, tangent, curvature);
        }

        /// <summary>
        /// Gets Greville points for this curve.
        /// </summary>
        /// <param name="all">If true, then all Greville points are returns. If false, only edit points are returned.</param>
        /// <returns>A list of points if successful, null otherwise.</returns>
        public static Point3dList GrevillePoints(this NurbsCurve nurbscurve, bool all)
        {
            return ComputeServer.Post<Point3dList>(ApiAddress(), nurbscurve, all);
        }

        /// <summary>
        /// Gets Greville points for this curve.
        /// </summary>
        /// <param name="all">If true, then all Greville points are returns. If false, only edit points are returned.</param>
        /// <returns>A list of points if successful, null otherwise.</returns>
        public static Point3dList GrevillePoints(Remote<NurbsCurve> nurbscurve, bool all)
        {
            return ComputeServer.Post<Point3dList>(ApiAddress(), nurbscurve, all);
        }

        /// <summary>
        /// Sets all Greville edit points for this curve.
        /// </summary>
        /// <param name="points">
        /// The new point locations. The number of points should match 
        /// the number of point returned by NurbsCurve.GrevillePoints(false).
        /// </param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool SetGrevillePoints(this NurbsCurve nurbscurve, out NurbsCurve updatedInstance, IEnumerable<Point3d> points)
        {
            return ComputeServer.Post<bool, NurbsCurve>(ApiAddress(), out updatedInstance, nurbscurve, points);
        }

        /// <summary>
        /// Sets all Greville edit points for this curve.
        /// </summary>
        /// <param name="points">
        /// The new point locations. The number of points should match 
        /// the number of point returned by NurbsCurve.GrevillePoints(false).
        /// </param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool SetGrevillePoints(Remote<NurbsCurve> nurbscurve, out NurbsCurve updatedInstance, IEnumerable<Point3d> points)
        {
            return ComputeServer.Post<bool, NurbsCurve>(ApiAddress(), out updatedInstance, nurbscurve, points);
        }

        /// <summary>
        /// Creates a C1 cubic NURBS approximation of a helix or spiral. For a helix,
        /// you may have radius0 == radius1. For a spiral radius0 == radius1 produces
        /// a circle. Zero and negative radii are permissible.
        /// </summary>
        /// <param name="axisStart">Helix's axis starting point or center of spiral.</param>
        /// <param name="axisDir">Helix's axis vector or normal to spiral's plane.</param>
        /// <param name="radiusPoint">
        /// Point used only to get a vector that is perpendicular to the axis. In
        /// particular, this vector must not be (anti)parallel to the axis vector.
        /// </param>
        /// <param name="pitch">
        /// The pitch, where a spiral has a pitch = 0, and pitch > 0 is the distance
        /// between the helix's "threads".
        /// </param>
        /// <param name="turnCount">The number of turns in spiral or helix. Positive
        /// values produce counter-clockwise orientation, negative values produce
        /// clockwise orientation. Note, for a helix, turnCount * pitch = length of
        /// the helix's axis.
        /// </param>
        /// <param name="radius0">The starting radius.</param>
        /// <param name="radius1">The ending radius.</param>
        /// <returns>NurbsCurve on success, null on failure.</returns>
        public static NurbsCurve CreateSpiral(Point3d axisStart, Vector3d axisDir, Point3d radiusPoint, double pitch, double turnCount, double radius0, double radius1)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), axisStart, axisDir, radiusPoint, pitch, turnCount, radius0, radius1);
        }

        /// <summary>
        /// Create a C2 non-rational uniform cubic NURBS approximation of a swept helix or spiral.
        /// </summary>
        /// <param name="railCurve">The rail curve.</param>
        /// <param name="t0">Starting portion of rail curve's domain to sweep along.</param>
        /// <param name="t1">Ending portion of rail curve's domain to sweep along.</param>
        /// <param name="radiusPoint">
        /// Point used only to get a vector that is perpendicular to the axis. In
        /// particular, this vector must not be (anti)parallel to the axis vector.
        /// </param>
        /// <param name="pitch">
        /// The pitch. Positive values produce counter-clockwise orientation,
        /// negative values produce clockwise orientation.
        /// </param>
        /// <param name="turnCount">
        /// The turn count. If != 0, then the resulting helix will have this many
        /// turns. If = 0, then pitch must be != 0 and the approximate distance
        /// between turns will be set to pitch. Positive values produce counter-clockwise
        /// orientation, negative values produce clockwise orientation.
        /// </param>
        /// <param name="radius0">
        /// The starting radius. At least one radii must be nonzero. Negative values
        /// are allowed.
        /// </param>
        /// <param name="radius1">
        /// The ending radius. At least one radii must be nonzero. Negative values
        /// are allowed.
        /// </param>
        /// <param name="pointsPerTurn">
        /// Number of points to interpolate per turn. Must be greater than 4.
        /// When in doubt, use 12.
        /// </param>
        /// <returns>NurbsCurve on success, null on failure.</returns>
        public static NurbsCurve CreateSpiral(Curve railCurve, double t0, double t1, Point3d radiusPoint, double pitch, double turnCount, double radius0, double radius1, int pointsPerTurn)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), railCurve, t0, t1, radiusPoint, pitch, turnCount, radius0, radius1, pointsPerTurn);
        }

        /// <summary>
        /// Create a C2 non-rational uniform cubic NURBS approximation of a swept helix or spiral.
        /// </summary>
        /// <param name="railCurve">The rail curve.</param>
        /// <param name="t0">Starting portion of rail curve's domain to sweep along.</param>
        /// <param name="t1">Ending portion of rail curve's domain to sweep along.</param>
        /// <param name="radiusPoint">
        /// Point used only to get a vector that is perpendicular to the axis. In
        /// particular, this vector must not be (anti)parallel to the axis vector.
        /// </param>
        /// <param name="pitch">
        /// The pitch. Positive values produce counter-clockwise orientation,
        /// negative values produce clockwise orientation.
        /// </param>
        /// <param name="turnCount">
        /// The turn count. If != 0, then the resulting helix will have this many
        /// turns. If = 0, then pitch must be != 0 and the approximate distance
        /// between turns will be set to pitch. Positive values produce counter-clockwise
        /// orientation, negative values produce clockwise orientation.
        /// </param>
        /// <param name="radius0">
        /// The starting radius. At least one radii must be nonzero. Negative values
        /// are allowed.
        /// </param>
        /// <param name="radius1">
        /// The ending radius. At least one radii must be nonzero. Negative values
        /// are allowed.
        /// </param>
        /// <param name="pointsPerTurn">
        /// Number of points to interpolate per turn. Must be greater than 4.
        /// When in doubt, use 12.
        /// </param>
        /// <returns>NurbsCurve on success, null on failure.</returns>
        public static NurbsCurve CreateSpiral(Remote<Curve> railCurve, double t0, double t1, Point3d radiusPoint, double pitch, double turnCount, double radius0, double radius1, int pointsPerTurn)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), railCurve, t0, t1, radiusPoint, pitch, turnCount, radius0, radius1, pointsPerTurn);
        }
    }

    public static class CurveCompute
    {
        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return ComputeServer.ApiAddress(typeof(Curve), caller);
        }

        /// <summary>
        /// Returns the type of conic section based on the curve's shape.
        /// </summary>
        /// <returns></returns>
        public static ConicSectionType GetConicSectionType(this Curve curve)
        {
            return ComputeServer.Post<ConicSectionType>(ApiAddress(), curve);
        }

        /// <summary>
        /// Returns the type of conic section based on the curve's shape.
        /// </summary>
        /// <returns></returns>
        public static ConicSectionType GetConicSectionType(Remote<Curve> curve)
        {
            return ComputeServer.Post<ConicSectionType>(ApiAddress(), curve);
        }

        /// <summary>
        /// Interpolates a sequence of points. Used by InterpCurve Command
        /// This routine works best when degree=3.
        /// </summary>
        /// <param name="degree">The degree of the curve >=1.  Degree must be odd.</param>
        /// <param name="points">
        /// Points to interpolate (Count must be >= 2)
        /// </param>
        /// <returns>interpolated curve on success. null on failure.</returns>
        public static Curve CreateInterpolatedCurve(IEnumerable<Point3d> points, int degree)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), points, degree);
        }

        /// <summary>
        /// Interpolates a sequence of points. Used by InterpCurve Command
        /// This routine works best when degree=3.
        /// </summary>
        /// <param name="degree">The degree of the curve >=1.  Degree must be odd.</param>
        /// <param name="points">
        /// Points to interpolate. For periodic curves if the final point is a
        /// duplicate of the initial point it is  ignored. (Count must be >=2)
        /// </param>
        /// <param name="knots">
        /// Knot-style to use  and specifies if the curve should be periodic.
        /// </param>
        /// <returns>interpolated curve on success. null on failure.</returns>
        public static Curve CreateInterpolatedCurve(IEnumerable<Point3d> points, int degree, CurveKnotStyle knots)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), points, degree, knots);
        }

        /// <summary>
        /// Interpolates a sequence of points. Used by InterpCurve Command
        /// This routine works best when degree=3.
        /// </summary>
        /// <param name="degree">The degree of the curve >=1.  Degree must be odd.</param>
        /// <param name="points">
        /// Points to interpolate. For periodic curves if the final point is a
        /// duplicate of the initial point it is  ignored. (Count must be >=2)
        /// </param>
        /// <param name="knots">
        /// Knot-style to use  and specifies if the curve should be periodic.
        /// </param>
        /// <param name="startTangent">A starting tangent.</param>
        /// <param name="endTangent">An ending tangent.</param>
        /// <returns>interpolated curve on success. null on failure.</returns>
        public static Curve CreateInterpolatedCurve(IEnumerable<Point3d> points, int degree, CurveKnotStyle knots, Vector3d startTangent, Vector3d endTangent)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), points, degree, knots, startTangent, endTangent);
        }

        /// <summary>
        /// Creates a soft edited curve from an existing curve using a smooth field of influence.
        /// </summary>
        /// <param name="curve">The curve to soft edit.</param>
        /// <param name="t">
        /// A parameter on the curve to move from. This location on the curve is moved, and the move
        ///  is smoothly tapered off with increasing distance along the curve from this parameter.
        /// </param>
        /// <param name="delta">The direction and magnitude, or maximum distance, of the move.</param>
        /// <param name="length">
        /// The distance along the curve from the editing point over which the strength 
        /// of the editing falls off smoothly.
        /// </param>
        /// <param name="fixEnds"></param>
        /// <returns>The soft edited curve if successful. null on failure.</returns>
        public static Curve CreateSoftEditCurve(Curve curve, double t, Vector3d delta, double length, bool fixEnds)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, t, delta, length, fixEnds);
        }

        /// <summary>
        /// Creates a soft edited curve from an existing curve using a smooth field of influence.
        /// </summary>
        /// <param name="curve">The curve to soft edit.</param>
        /// <param name="t">
        /// A parameter on the curve to move from. This location on the curve is moved, and the move
        ///  is smoothly tapered off with increasing distance along the curve from this parameter.
        /// </param>
        /// <param name="delta">The direction and magnitude, or maximum distance, of the move.</param>
        /// <param name="length">
        /// The distance along the curve from the editing point over which the strength 
        /// of the editing falls off smoothly.
        /// </param>
        /// <param name="fixEnds"></param>
        /// <returns>The soft edited curve if successful. null on failure.</returns>
        public static Curve CreateSoftEditCurve(Remote<Curve> curve, double t, Vector3d delta, double length, bool fixEnds)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, t, delta, length, fixEnds);
        }

        /// <summary>
        /// Rounds the corners of a kinked curve with arcs of a single, specified radius.
        /// </summary>
        /// <param name="curve">The curve to fillet.</param>
        /// <param name="radius">The fillet radius.</param>
        /// <param name="tolerance">The tolerance. When in doubt, use the document's model space absolute tolerance.</param>
        /// <param name="angleTolerance">The angle tolerance in radians. When in doubt, use the document's model space angle tolerance.</param>
        /// <returns>The filleted curve if successful. null on failure.</returns>
        public static Curve CreateFilletCornersCurve(Curve curve, double radius, double tolerance, double angleTolerance)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, radius, tolerance, angleTolerance);
        }

        /// <summary>
        /// Rounds the corners of a kinked curve with arcs of a single, specified radius.
        /// </summary>
        /// <param name="curve">The curve to fillet.</param>
        /// <param name="radius">The fillet radius.</param>
        /// <param name="tolerance">The tolerance. When in doubt, use the document's model space absolute tolerance.</param>
        /// <param name="angleTolerance">The angle tolerance in radians. When in doubt, use the document's model space angle tolerance.</param>
        /// <returns>The filleted curve if successful. null on failure.</returns>
        public static Curve CreateFilletCornersCurve(Remote<Curve> curve, double radius, double tolerance, double angleTolerance)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, radius, tolerance, angleTolerance);
        }

        /// <summary>
        /// Creates a polycurve consisting of two tangent arc segments that connect two points and two directions.
        /// </summary>
        /// <param name="startPt">Start of the arc blend curve.</param>
        /// <param name="startDir">Start direction of the arc blend curve.</param>
        /// <param name="endPt">End of the arc blend curve.</param>
        /// <param name="endDir">End direction of the arc blend curve.</param>
        /// <param name="controlPointLengthRatio">
        /// The ratio of the control polygon lengths of the two arcs. Note, a value of 1.0 
        /// means the control polygon lengths for both arcs will be the same.
        /// </param>
        /// <returns>The arc blend curve, or null on error.</returns>
        public static Curve CreateArcBlend(Point3d startPt, Vector3d startDir, Point3d endPt, Vector3d endDir, double controlPointLengthRatio)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), startPt, startDir, endPt, endDir, controlPointLengthRatio);
        }

        /// <summary>
        /// Constructs a mean, or average, curve from two curves.
        /// </summary>
        /// <param name="curveA">A first curve.</param>
        /// <param name="curveB">A second curve.</param>
        /// <param name="angleToleranceRadians">
        /// The angle tolerance, in radians, used to match kinks between curves.
        /// If you are unsure how to set this parameter, then either use the
        /// document's angle tolerance RhinoDoc.AngleToleranceRadians,
        /// or the default value (RhinoMath.UnsetValue)
        /// </param>
        /// <returns>The average curve, or null on error.</returns>
        /// <exception cref="ArgumentNullException">If curveA or curveB are null.</exception>
        public static Curve CreateMeanCurve(Curve curveA, Curve curveB, double angleToleranceRadians)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curveA, curveB, angleToleranceRadians);
        }

        /// <summary>
        /// Constructs a mean, or average, curve from two curves.
        /// </summary>
        /// <param name="curveA">A first curve.</param>
        /// <param name="curveB">A second curve.</param>
        /// <param name="angleToleranceRadians">
        /// The angle tolerance, in radians, used to match kinks between curves.
        /// If you are unsure how to set this parameter, then either use the
        /// document's angle tolerance RhinoDoc.AngleToleranceRadians,
        /// or the default value (RhinoMath.UnsetValue)
        /// </param>
        /// <returns>The average curve, or null on error.</returns>
        /// <exception cref="ArgumentNullException">If curveA or curveB are null.</exception>
        public static Curve CreateMeanCurve(Remote<Curve> curveA, Remote<Curve> curveB, double angleToleranceRadians)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curveA, curveB, angleToleranceRadians);
        }

        /// <summary>
        /// Constructs a mean, or average, curve from two curves.
        /// </summary>
        /// <param name="curveA">A first curve.</param>
        /// <param name="curveB">A second curve.</param>
        /// <returns>The average curve, or null on error.</returns>
        /// <exception cref="ArgumentNullException">If curveA or curveB are null.</exception>
        public static Curve CreateMeanCurve(Curve curveA, Curve curveB)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curveA, curveB);
        }

        /// <summary>
        /// Constructs a mean, or average, curve from two curves.
        /// </summary>
        /// <param name="curveA">A first curve.</param>
        /// <param name="curveB">A second curve.</param>
        /// <returns>The average curve, or null on error.</returns>
        /// <exception cref="ArgumentNullException">If curveA or curveB are null.</exception>
        public static Curve CreateMeanCurve(Remote<Curve> curveA, Remote<Curve> curveB)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curveA, curveB);
        }

        /// <summary>
        /// Create a Blend curve between two existing curves.
        /// </summary>
        /// <param name="curveA">Curve to blend from (blending will occur at curve end point).</param>
        /// <param name="curveB">Curve to blend to (blending will occur at curve start point).</param>
        /// <param name="continuity">Continuity of blend.</param>
        /// <returns>A curve representing the blend between A and B or null on failure.</returns>
        public static Curve CreateBlendCurve(Curve curveA, Curve curveB, BlendContinuity continuity)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curveA, curveB, continuity);
        }

        /// <summary>
        /// Create a Blend curve between two existing curves.
        /// </summary>
        /// <param name="curveA">Curve to blend from (blending will occur at curve end point).</param>
        /// <param name="curveB">Curve to blend to (blending will occur at curve start point).</param>
        /// <param name="continuity">Continuity of blend.</param>
        /// <returns>A curve representing the blend between A and B or null on failure.</returns>
        public static Curve CreateBlendCurve(Remote<Curve> curveA, Remote<Curve> curveB, BlendContinuity continuity)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curveA, curveB, continuity);
        }

        /// <summary>
        /// Create a Blend curve between two existing curves.
        /// </summary>
        /// <param name="curveA">Curve to blend from (blending will occur at curve end point).</param>
        /// <param name="curveB">Curve to blend to (blending will occur at curve start point).</param>
        /// <param name="continuity">Continuity of blend.</param>
        /// <param name="bulgeA">Bulge factor at curveA end of blend. Values near 1.0 work best.</param>
        /// <param name="bulgeB">Bulge factor at curveB end of blend. Values near 1.0 work best.</param>
        /// <returns>A curve representing the blend between A and B or null on failure.</returns>
        public static Curve CreateBlendCurve(Curve curveA, Curve curveB, BlendContinuity continuity, double bulgeA, double bulgeB)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curveA, curveB, continuity, bulgeA, bulgeB);
        }

        /// <summary>
        /// Create a Blend curve between two existing curves.
        /// </summary>
        /// <param name="curveA">Curve to blend from (blending will occur at curve end point).</param>
        /// <param name="curveB">Curve to blend to (blending will occur at curve start point).</param>
        /// <param name="continuity">Continuity of blend.</param>
        /// <param name="bulgeA">Bulge factor at curveA end of blend. Values near 1.0 work best.</param>
        /// <param name="bulgeB">Bulge factor at curveB end of blend. Values near 1.0 work best.</param>
        /// <returns>A curve representing the blend between A and B or null on failure.</returns>
        public static Curve CreateBlendCurve(Remote<Curve> curveA, Remote<Curve> curveB, BlendContinuity continuity, double bulgeA, double bulgeB)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curveA, curveB, continuity, bulgeA, bulgeB);
        }

        /// <summary>
        /// Makes a curve blend between 2 curves at the parameters specified
        /// with the directions and continuities specified
        /// </summary>
        /// <param name="curve0">First curve to blend from</param>
        /// <param name="t0">Parameter on first curve for blend endpoint</param>
        /// <param name="reverse0">
        /// If false, the blend will go in the natural direction of the curve.
        /// If true, the blend will go in the opposite direction to the curve
        /// </param>
        /// <param name="continuity0">Continuity for the blend at the start</param>
        /// <param name="curve1">Second curve to blend from</param>
        /// <param name="t1">Parameter on second curve for blend endpoint</param>
        /// <param name="reverse1">
        /// If false, the blend will go in the natural direction of the curve.
        /// If true, the blend will go in the opposite direction to the curve
        /// </param>
        /// <param name="continuity1">Continuity for the blend at the end</param>
        /// <returns>The blend curve on success. null on failure</returns>
        public static Curve CreateBlendCurve(Curve curve0, double t0, bool reverse0, BlendContinuity continuity0, Curve curve1, double t1, bool reverse1, BlendContinuity continuity1)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve0, t0, reverse0, continuity0, curve1, t1, reverse1, continuity1);
        }

        /// <summary>
        /// Makes a curve blend between 2 curves at the parameters specified
        /// with the directions and continuities specified
        /// </summary>
        /// <param name="curve0">First curve to blend from</param>
        /// <param name="t0">Parameter on first curve for blend endpoint</param>
        /// <param name="reverse0">
        /// If false, the blend will go in the natural direction of the curve.
        /// If true, the blend will go in the opposite direction to the curve
        /// </param>
        /// <param name="continuity0">Continuity for the blend at the start</param>
        /// <param name="curve1">Second curve to blend from</param>
        /// <param name="t1">Parameter on second curve for blend endpoint</param>
        /// <param name="reverse1">
        /// If false, the blend will go in the natural direction of the curve.
        /// If true, the blend will go in the opposite direction to the curve
        /// </param>
        /// <param name="continuity1">Continuity for the blend at the end</param>
        /// <returns>The blend curve on success. null on failure</returns>
        public static Curve CreateBlendCurve(Remote<Curve> curve0, double t0, bool reverse0, BlendContinuity continuity0, Remote<Curve> curve1, double t1, bool reverse1, BlendContinuity continuity1)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve0, t0, reverse0, continuity0, curve1, t1, reverse1, continuity1);
        }

        /// <summary>
        /// Creates curves between two open or closed input curves. Uses the control points of the curves for finding tween curves.
        /// That means the first control point of first curve is matched to first control point of the second curve and so on.
        /// There is no matching of curves direction. Caller must match input curves direction before calling the function.
        /// </summary>
        /// <param name="curve0">The first, or starting, curve.</param>
        /// <param name="curve1">The second, or ending, curve.</param>
        /// <param name="numCurves">Number of tween curves to create.</param>
        /// <returns>An array of joint curves. This array can be empty.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] CreateTweenCurves(Curve curve0, Curve curve1, int numCurves)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve0, curve1, numCurves);
        }

        /// <summary>
        /// Creates curves between two open or closed input curves. Uses the control points of the curves for finding tween curves.
        /// That means the first control point of first curve is matched to first control point of the second curve and so on.
        /// There is no matching of curves direction. Caller must match input curves direction before calling the function.
        /// </summary>
        /// <param name="curve0">The first, or starting, curve.</param>
        /// <param name="curve1">The second, or ending, curve.</param>
        /// <param name="numCurves">Number of tween curves to create.</param>
        /// <returns>An array of joint curves. This array can be empty.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] CreateTweenCurves(Remote<Curve> curve0, Remote<Curve> curve1, int numCurves)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve0, curve1, numCurves);
        }

        /// <summary>
        /// Creates curves between two open or closed input curves. Uses the control points of the curves for finding tween curves.
        /// That means the first control point of first curve is matched to first control point of the second curve and so on.
        /// There is no matching of curves direction. Caller must match input curves direction before calling the function.
        /// </summary>
        /// <param name="curve0">The first, or starting, curve.</param>
        /// <param name="curve1">The second, or ending, curve.</param>
        /// <param name="numCurves">Number of tween curves to create.</param>
        /// <param name="tolerance"></param>
        /// <returns>An array of joint curves. This array can be empty.</returns>
        public static Curve[] CreateTweenCurves(Curve curve0, Curve curve1, int numCurves, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve0, curve1, numCurves, tolerance);
        }

        /// <summary>
        /// Creates curves between two open or closed input curves. Uses the control points of the curves for finding tween curves.
        /// That means the first control point of first curve is matched to first control point of the second curve and so on.
        /// There is no matching of curves direction. Caller must match input curves direction before calling the function.
        /// </summary>
        /// <param name="curve0">The first, or starting, curve.</param>
        /// <param name="curve1">The second, or ending, curve.</param>
        /// <param name="numCurves">Number of tween curves to create.</param>
        /// <param name="tolerance"></param>
        /// <returns>An array of joint curves. This array can be empty.</returns>
        public static Curve[] CreateTweenCurves(Remote<Curve> curve0, Remote<Curve> curve1, int numCurves, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve0, curve1, numCurves, tolerance);
        }

        /// <summary>
        /// Creates curves between two open or closed input curves. Make the structure of input curves compatible if needed.
        /// Refits the input curves to have the same structure. The resulting curves are usually more complex than input unless
        /// input curves are compatible and no refit is needed. There is no matching of curves direction.
        /// Caller must match input curves direction before calling the function.
        /// </summary>
        /// <param name="curve0">The first, or starting, curve.</param>
        /// <param name="curve1">The second, or ending, curve.</param>
        /// <param name="numCurves">Number of tween curves to create.</param>
        /// <returns>An array of joint curves. This array can be empty.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] CreateTweenCurvesWithMatching(Curve curve0, Curve curve1, int numCurves)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve0, curve1, numCurves);
        }

        /// <summary>
        /// Creates curves between two open or closed input curves. Make the structure of input curves compatible if needed.
        /// Refits the input curves to have the same structure. The resulting curves are usually more complex than input unless
        /// input curves are compatible and no refit is needed. There is no matching of curves direction.
        /// Caller must match input curves direction before calling the function.
        /// </summary>
        /// <param name="curve0">The first, or starting, curve.</param>
        /// <param name="curve1">The second, or ending, curve.</param>
        /// <param name="numCurves">Number of tween curves to create.</param>
        /// <returns>An array of joint curves. This array can be empty.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] CreateTweenCurvesWithMatching(Remote<Curve> curve0, Remote<Curve> curve1, int numCurves)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve0, curve1, numCurves);
        }

        /// <summary>
        /// Creates curves between two open or closed input curves. Make the structure of input curves compatible if needed.
        /// Refits the input curves to have the same structure. The resulting curves are usually more complex than input unless
        /// input curves are compatible and no refit is needed. There is no matching of curves direction.
        /// Caller must match input curves direction before calling the function.
        /// </summary>
        /// <param name="curve0">The first, or starting, curve.</param>
        /// <param name="curve1">The second, or ending, curve.</param>
        /// <param name="numCurves">Number of tween curves to create.</param>
        /// <param name="tolerance"></param>
        /// <returns>An array of joint curves. This array can be empty.</returns>
        public static Curve[] CreateTweenCurvesWithMatching(Curve curve0, Curve curve1, int numCurves, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve0, curve1, numCurves, tolerance);
        }

        /// <summary>
        /// Creates curves between two open or closed input curves. Make the structure of input curves compatible if needed.
        /// Refits the input curves to have the same structure. The resulting curves are usually more complex than input unless
        /// input curves are compatible and no refit is needed. There is no matching of curves direction.
        /// Caller must match input curves direction before calling the function.
        /// </summary>
        /// <param name="curve0">The first, or starting, curve.</param>
        /// <param name="curve1">The second, or ending, curve.</param>
        /// <param name="numCurves">Number of tween curves to create.</param>
        /// <param name="tolerance"></param>
        /// <returns>An array of joint curves. This array can be empty.</returns>
        public static Curve[] CreateTweenCurvesWithMatching(Remote<Curve> curve0, Remote<Curve> curve1, int numCurves, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve0, curve1, numCurves, tolerance);
        }

        /// <summary>
        /// Creates curves between two open or closed input curves. Use sample points method to make curves compatible.
        /// This is how the algorithm works: Divides the two curves into an equal number of points, finds the midpoint between the 
        /// corresponding points on the curves and interpolates the tween curve through those points. There is no matching of curves
        /// direction. Caller must match input curves direction before calling the function.
        /// </summary>
        /// <param name="curve0">The first, or starting, curve.</param>
        /// <param name="curve1">The second, or ending, curve.</param>
        /// <param name="numCurves">Number of tween curves to create.</param>
        /// <param name="numSamples">Number of sample points along input curves.</param>
        /// <returns>>An array of joint curves. This array can be empty.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] CreateTweenCurvesWithSampling(Curve curve0, Curve curve1, int numCurves, int numSamples)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve0, curve1, numCurves, numSamples);
        }

        /// <summary>
        /// Creates curves between two open or closed input curves. Use sample points method to make curves compatible.
        /// This is how the algorithm works: Divides the two curves into an equal number of points, finds the midpoint between the 
        /// corresponding points on the curves and interpolates the tween curve through those points. There is no matching of curves
        /// direction. Caller must match input curves direction before calling the function.
        /// </summary>
        /// <param name="curve0">The first, or starting, curve.</param>
        /// <param name="curve1">The second, or ending, curve.</param>
        /// <param name="numCurves">Number of tween curves to create.</param>
        /// <param name="numSamples">Number of sample points along input curves.</param>
        /// <returns>>An array of joint curves. This array can be empty.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] CreateTweenCurvesWithSampling(Remote<Curve> curve0, Remote<Curve> curve1, int numCurves, int numSamples)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve0, curve1, numCurves, numSamples);
        }

        /// <summary>
        /// Creates curves between two open or closed input curves. Use sample points method to make curves compatible.
        /// This is how the algorithm works: Divides the two curves into an equal number of points, finds the midpoint between the 
        /// corresponding points on the curves and interpolates the tween curve through those points. There is no matching of curves
        /// direction. Caller must match input curves direction before calling the function.
        /// </summary>
        /// <param name="curve0">The first, or starting, curve.</param>
        /// <param name="curve1">The second, or ending, curve.</param>
        /// <param name="numCurves">Number of tween curves to create.</param>
        /// <param name="numSamples">Number of sample points along input curves.</param>
        /// <param name="tolerance"></param>
        /// <returns>>An array of joint curves. This array can be empty.</returns>
        public static Curve[] CreateTweenCurvesWithSampling(Curve curve0, Curve curve1, int numCurves, int numSamples, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve0, curve1, numCurves, numSamples, tolerance);
        }

        /// <summary>
        /// Creates curves between two open or closed input curves. Use sample points method to make curves compatible.
        /// This is how the algorithm works: Divides the two curves into an equal number of points, finds the midpoint between the 
        /// corresponding points on the curves and interpolates the tween curve through those points. There is no matching of curves
        /// direction. Caller must match input curves direction before calling the function.
        /// </summary>
        /// <param name="curve0">The first, or starting, curve.</param>
        /// <param name="curve1">The second, or ending, curve.</param>
        /// <param name="numCurves">Number of tween curves to create.</param>
        /// <param name="numSamples">Number of sample points along input curves.</param>
        /// <param name="tolerance"></param>
        /// <returns>>An array of joint curves. This array can be empty.</returns>
        public static Curve[] CreateTweenCurvesWithSampling(Remote<Curve> curve0, Remote<Curve> curve1, int numCurves, int numSamples, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve0, curve1, numCurves, numSamples, tolerance);
        }

        /// <summary>
        /// Joins a collection of curve segments together.
        /// </summary>
        /// <param name="inputCurves">Curve segments to join.</param>
        /// <returns>An array of curves which contains.</returns>
        public static Curve[] JoinCurves(IEnumerable<Curve> inputCurves)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), inputCurves);
        }

        /// <summary>
        /// Joins a collection of curve segments together.
        /// </summary>
        /// <param name="inputCurves">Curve segments to join.</param>
        /// <returns>An array of curves which contains.</returns>
        public static Curve[] JoinCurves(Remote<IEnumerable<Curve>> inputCurves)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), inputCurves);
        }

        /// <summary>
        /// Joins a collection of curve segments together.
        /// </summary>
        /// <param name="inputCurves">An array, a list or any enumerable set of curve segments to join.</param>
        /// <param name="joinTolerance">Joining tolerance, 
        /// i.e. the distance between segment end-points that is allowed.</param>
        /// <returns>An array of joint curves. This array can be empty.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_dividebylength.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_dividebylength.cs' lang='cs'/>
        /// <code source='examples\py\ex_dividebylength.py' lang='py'/>
        /// </example>
        /// <exception cref="ArgumentNullException">If inputCurves is null.</exception>
        public static Curve[] JoinCurves(IEnumerable<Curve> inputCurves, double joinTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), inputCurves, joinTolerance);
        }

        /// <summary>
        /// Joins a collection of curve segments together.
        /// </summary>
        /// <param name="inputCurves">An array, a list or any enumerable set of curve segments to join.</param>
        /// <param name="joinTolerance">Joining tolerance, 
        /// i.e. the distance between segment end-points that is allowed.</param>
        /// <returns>An array of joint curves. This array can be empty.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_dividebylength.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_dividebylength.cs' lang='cs'/>
        /// <code source='examples\py\ex_dividebylength.py' lang='py'/>
        /// </example>
        /// <exception cref="ArgumentNullException">If inputCurves is null.</exception>
        public static Curve[] JoinCurves(Remote<IEnumerable<Curve>> inputCurves, double joinTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), inputCurves, joinTolerance);
        }

        /// <summary>
        /// Joins a collection of curve segments together.
        /// </summary>
        /// <param name="inputCurves">An array, a list or any enumerable set of curve segments to join.</param>
        /// <param name="joinTolerance">Joining tolerance, 
        /// i.e. the distance between segment end-points that is allowed.</param>
        /// <param name="preserveDirection">
        /// <para>If true, curve endpoints will be compared to curve start points.</para>
        /// <para>If false, all start and endpoints will be compared and copies of input curves may be reversed in output.</para>
        /// </param>
        /// <returns>An array of joint curves. This array can be empty.</returns>
        /// <exception cref="ArgumentNullException">If inputCurves is null.</exception>
        public static Curve[] JoinCurves(IEnumerable<Curve> inputCurves, double joinTolerance, bool preserveDirection)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), inputCurves, joinTolerance, preserveDirection);
        }

        /// <summary>
        /// Joins a collection of curve segments together.
        /// </summary>
        /// <param name="inputCurves">An array, a list or any enumerable set of curve segments to join.</param>
        /// <param name="joinTolerance">Joining tolerance, 
        /// i.e. the distance between segment end-points that is allowed.</param>
        /// <param name="preserveDirection">
        /// <para>If true, curve endpoints will be compared to curve start points.</para>
        /// <para>If false, all start and endpoints will be compared and copies of input curves may be reversed in output.</para>
        /// </param>
        /// <returns>An array of joint curves. This array can be empty.</returns>
        /// <exception cref="ArgumentNullException">If inputCurves is null.</exception>
        public static Curve[] JoinCurves(Remote<IEnumerable<Curve>> inputCurves, double joinTolerance, bool preserveDirection)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), inputCurves, joinTolerance, preserveDirection);
        }

        /// <summary>
        /// Makes adjustments to the ends of one or both input curves so that they meet at a point.
        /// </summary>
        /// <param name="curveA">1st curve to adjust.</param>
        /// <param name="adjustStartCurveA">
        /// Which end of the 1st curve to adjust: true is start, false is end.
        /// </param>
        /// <param name="curveB">2nd curve to adjust.</param>
        /// <param name="adjustStartCurveB">
        /// which end of the 2nd curve to adjust true==start, false==end.
        /// </param>
        /// <returns>true on success.</returns>
        public static bool MakeEndsMeet(Curve curveA, bool adjustStartCurveA, Curve curveB, bool adjustStartCurveB)
        {
            return ComputeServer.Post<bool>(ApiAddress(), curveA, adjustStartCurveA, curveB, adjustStartCurveB);
        }

        /// <summary>
        /// Makes adjustments to the ends of one or both input curves so that they meet at a point.
        /// </summary>
        /// <param name="curveA">1st curve to adjust.</param>
        /// <param name="adjustStartCurveA">
        /// Which end of the 1st curve to adjust: true is start, false is end.
        /// </param>
        /// <param name="curveB">2nd curve to adjust.</param>
        /// <param name="adjustStartCurveB">
        /// which end of the 2nd curve to adjust true==start, false==end.
        /// </param>
        /// <returns>true on success.</returns>
        public static bool MakeEndsMeet(Remote<Curve> curveA, bool adjustStartCurveA, Remote<Curve> curveB, bool adjustStartCurveB)
        {
            return ComputeServer.Post<bool>(ApiAddress(), curveA, adjustStartCurveA, curveB, adjustStartCurveB);
        }

        /// <summary>
        /// Computes the fillet arc for a curve filleting operation.
        /// </summary>
        /// <param name="curve0">First curve to fillet.</param>
        /// <param name="curve1">Second curve to fillet.</param>
        /// <param name="radius">Fillet radius.</param>
        /// <param name="t0Base">Parameter on curve0 where the fillet ought to start (approximately).</param>
        /// <param name="t1Base">Parameter on curve1 where the fillet ought to end (approximately).</param>
        /// <returns>The fillet arc on success, or Arc.Unset on failure.</returns>
        public static Arc CreateFillet(Curve curve0, Curve curve1, double radius, double t0Base, double t1Base)
        {
            return ComputeServer.Post<Arc>(ApiAddress(), curve0, curve1, radius, t0Base, t1Base);
        }

        /// <summary>
        /// Computes the fillet arc for a curve filleting operation.
        /// </summary>
        /// <param name="curve0">First curve to fillet.</param>
        /// <param name="curve1">Second curve to fillet.</param>
        /// <param name="radius">Fillet radius.</param>
        /// <param name="t0Base">Parameter on curve0 where the fillet ought to start (approximately).</param>
        /// <param name="t1Base">Parameter on curve1 where the fillet ought to end (approximately).</param>
        /// <returns>The fillet arc on success, or Arc.Unset on failure.</returns>
        public static Arc CreateFillet(Remote<Curve> curve0, Remote<Curve> curve1, double radius, double t0Base, double t1Base)
        {
            return ComputeServer.Post<Arc>(ApiAddress(), curve0, curve1, radius, t0Base, t1Base);
        }

        /// <summary>
        /// Creates a tangent arc between two curves and trims or extends the curves to the arc.
        /// </summary>
        /// <param name="curve0">The first curve to fillet.</param>
        /// <param name="point0">
        /// A point on the first curve that is near the end where the fillet will
        /// be created.
        /// </param>
        /// <param name="curve1">The second curve to fillet.</param>
        /// <param name="point1">
        /// A point on the second curve that is near the end where the fillet will
        /// be created.
        /// </param>
        /// <param name="radius">The radius of the fillet.</param>
        /// <param name="join">Join the output curves.</param>
        /// <param name="trim">
        /// Trim copies of the input curves to the output fillet curve.
        /// </param>
        /// <param name="arcExtension">
        /// Applies when arcs are filleted but need to be extended to meet the
        /// fillet curve or chamfer line. If true, then the arc is extended
        /// maintaining its validity. If false, then the arc is extended with a
        /// line segment, which is joined to the arc converting it to a polycurve.
        /// </param>
        /// <param name="tolerance">
        /// The tolerance, generally the document's absolute tolerance.
        /// </param>
        /// <param name="angleTolerance"></param>
        /// <returns>
        /// The results of the fillet operation. The number of output curves depends
        /// on the input curves and the values of the parameters that were used
        /// during the fillet operation. In most cases, the output array will contain
        /// either one or three curves, although two curves can be returned if the
        /// radius is zero and join = false.
        /// For example, if both join and trim = true, then the output curve
        /// will be a polycurve containing the fillet curve joined with trimmed copies
        /// of the input curves. If join = false and trim = true, then three curves,
        /// the fillet curve and trimmed copies of the input curves, will be returned.
        /// If both join and trim = false, then just the fillet curve is returned.
        /// </returns>
        /// <example>
        /// <code source='examples\vbnet\ex_filletcurves.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_filletcurves.cs' lang='cs'/>
        /// <code source='examples\py\ex_filletcurves.py' lang='py'/>
        /// </example>
        public static Curve[] CreateFilletCurves(Curve curve0, Point3d point0, Curve curve1, Point3d point1, double radius, bool join, bool trim, bool arcExtension, double tolerance, double angleTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve0, point0, curve1, point1, radius, join, trim, arcExtension, tolerance, angleTolerance);
        }

        /// <summary>
        /// Creates a tangent arc between two curves and trims or extends the curves to the arc.
        /// </summary>
        /// <param name="curve0">The first curve to fillet.</param>
        /// <param name="point0">
        /// A point on the first curve that is near the end where the fillet will
        /// be created.
        /// </param>
        /// <param name="curve1">The second curve to fillet.</param>
        /// <param name="point1">
        /// A point on the second curve that is near the end where the fillet will
        /// be created.
        /// </param>
        /// <param name="radius">The radius of the fillet.</param>
        /// <param name="join">Join the output curves.</param>
        /// <param name="trim">
        /// Trim copies of the input curves to the output fillet curve.
        /// </param>
        /// <param name="arcExtension">
        /// Applies when arcs are filleted but need to be extended to meet the
        /// fillet curve or chamfer line. If true, then the arc is extended
        /// maintaining its validity. If false, then the arc is extended with a
        /// line segment, which is joined to the arc converting it to a polycurve.
        /// </param>
        /// <param name="tolerance">
        /// The tolerance, generally the document's absolute tolerance.
        /// </param>
        /// <param name="angleTolerance"></param>
        /// <returns>
        /// The results of the fillet operation. The number of output curves depends
        /// on the input curves and the values of the parameters that were used
        /// during the fillet operation. In most cases, the output array will contain
        /// either one or three curves, although two curves can be returned if the
        /// radius is zero and join = false.
        /// For example, if both join and trim = true, then the output curve
        /// will be a polycurve containing the fillet curve joined with trimmed copies
        /// of the input curves. If join = false and trim = true, then three curves,
        /// the fillet curve and trimmed copies of the input curves, will be returned.
        /// If both join and trim = false, then just the fillet curve is returned.
        /// </returns>
        /// <example>
        /// <code source='examples\vbnet\ex_filletcurves.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_filletcurves.cs' lang='cs'/>
        /// <code source='examples\py\ex_filletcurves.py' lang='py'/>
        /// </example>
        public static Curve[] CreateFilletCurves(Remote<Curve> curve0, Point3d point0, Remote<Curve> curve1, Point3d point1, double radius, bool join, bool trim, bool arcExtension, double tolerance, double angleTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve0, point0, curve1, point1, radius, join, trim, arcExtension, tolerance, angleTolerance);
        }

        /// <summary>
        /// Calculates the boolean union of two or more closed, planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curves">The co-planar curves to union.</param>
        /// <returns>Result curves on success, empty array if no union could be calculated.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] CreateBooleanUnion(IEnumerable<Curve> curves)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curves);
        }

        /// <summary>
        /// Calculates the boolean union of two or more closed, planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curves">The co-planar curves to union.</param>
        /// <returns>Result curves on success, empty array if no union could be calculated.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] CreateBooleanUnion(Remote<IEnumerable<Curve>> curves)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curves);
        }

        /// <summary>
        /// Calculates the boolean union of two or more closed, planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curves">The co-planar curves to union.</param>
        /// <param name="tolerance"></param>
        /// <returns>Result curves on success, empty array if no union could be calculated.</returns>
        public static Curve[] CreateBooleanUnion(IEnumerable<Curve> curves, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curves, tolerance);
        }

        /// <summary>
        /// Calculates the boolean union of two or more closed, planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curves">The co-planar curves to union.</param>
        /// <param name="tolerance"></param>
        /// <returns>Result curves on success, empty array if no union could be calculated.</returns>
        public static Curve[] CreateBooleanUnion(Remote<IEnumerable<Curve>> curves, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curves, tolerance);
        }

        /// <summary>
        /// Calculates the boolean intersection of two closed, planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curveA">The first closed, planar curve.</param>
        /// <param name="curveB">The second closed, planar curve.</param>
        /// <returns>Result curves on success, empty array if no intersection could be calculated.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] CreateBooleanIntersection(Curve curveA, Curve curveB)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curveA, curveB);
        }

        /// <summary>
        /// Calculates the boolean intersection of two closed, planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curveA">The first closed, planar curve.</param>
        /// <param name="curveB">The second closed, planar curve.</param>
        /// <returns>Result curves on success, empty array if no intersection could be calculated.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] CreateBooleanIntersection(Remote<Curve> curveA, Remote<Curve> curveB)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curveA, curveB);
        }

        /// <summary>
        /// Calculates the boolean intersection of two closed, planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curveA">The first closed, planar curve.</param>
        /// <param name="curveB">The second closed, planar curve.</param>
        /// <param name="tolerance"></param>
        /// <returns>Result curves on success, empty array if no intersection could be calculated.</returns>
        public static Curve[] CreateBooleanIntersection(Curve curveA, Curve curveB, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curveA, curveB, tolerance);
        }

        /// <summary>
        /// Calculates the boolean intersection of two closed, planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curveA">The first closed, planar curve.</param>
        /// <param name="curveB">The second closed, planar curve.</param>
        /// <param name="tolerance"></param>
        /// <returns>Result curves on success, empty array if no intersection could be calculated.</returns>
        public static Curve[] CreateBooleanIntersection(Remote<Curve> curveA, Remote<Curve> curveB, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curveA, curveB, tolerance);
        }

        /// <summary>
        /// Calculates the boolean difference between two closed, planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curveA">The first closed, planar curve.</param>
        /// <param name="curveB">The second closed, planar curve.</param>
        /// <returns>Result curves on success, empty array if no difference could be calculated.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] CreateBooleanDifference(Curve curveA, Curve curveB)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curveA, curveB);
        }

        /// <summary>
        /// Calculates the boolean difference between two closed, planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curveA">The first closed, planar curve.</param>
        /// <param name="curveB">The second closed, planar curve.</param>
        /// <returns>Result curves on success, empty array if no difference could be calculated.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] CreateBooleanDifference(Remote<Curve> curveA, Remote<Curve> curveB)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curveA, curveB);
        }

        /// <summary>
        /// Calculates the boolean difference between two closed, planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curveA">The first closed, planar curve.</param>
        /// <param name="curveB">The second closed, planar curve.</param>
        /// <param name="tolerance"></param>
        /// <returns>Result curves on success, empty array if no difference could be calculated.</returns>
        public static Curve[] CreateBooleanDifference(Curve curveA, Curve curveB, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curveA, curveB, tolerance);
        }

        /// <summary>
        /// Calculates the boolean difference between two closed, planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curveA">The first closed, planar curve.</param>
        /// <param name="curveB">The second closed, planar curve.</param>
        /// <param name="tolerance"></param>
        /// <returns>Result curves on success, empty array if no difference could be calculated.</returns>
        public static Curve[] CreateBooleanDifference(Remote<Curve> curveA, Remote<Curve> curveB, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curveA, curveB, tolerance);
        }

        /// <summary>
        /// Calculates the boolean difference between a closed planar curve, and a list of closed planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curveA">The first closed, planar curve.</param>
        /// <param name="subtractors">curves to subtract from the first closed curve.</param>
        /// <returns>Result curves on success, empty array if no difference could be calculated.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] CreateBooleanDifference(Curve curveA, IEnumerable<Curve> subtractors)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curveA, subtractors);
        }

        /// <summary>
        /// Calculates the boolean difference between a closed planar curve, and a list of closed planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curveA">The first closed, planar curve.</param>
        /// <param name="subtractors">curves to subtract from the first closed curve.</param>
        /// <returns>Result curves on success, empty array if no difference could be calculated.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] CreateBooleanDifference(Remote<Curve> curveA, Remote<IEnumerable<Curve>> subtractors)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curveA, subtractors);
        }

        /// <summary>
        /// Calculates the boolean difference between a closed planar curve, and a list of closed planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curveA">The first closed, planar curve.</param>
        /// <param name="subtractors">curves to subtract from the first closed curve.</param>
        /// <param name="tolerance"></param>
        /// <returns>Result curves on success, empty array if no difference could be calculated.</returns>
        public static Curve[] CreateBooleanDifference(Curve curveA, IEnumerable<Curve> subtractors, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curveA, subtractors, tolerance);
        }

        /// <summary>
        /// Calculates the boolean difference between a closed planar curve, and a list of closed planar curves. 
        /// Note, curves must be co-planar.
        /// </summary>
        /// <param name="curveA">The first closed, planar curve.</param>
        /// <param name="subtractors">curves to subtract from the first closed curve.</param>
        /// <param name="tolerance"></param>
        /// <returns>Result curves on success, empty array if no difference could be calculated.</returns>
        public static Curve[] CreateBooleanDifference(Remote<Curve> curveA, Remote<IEnumerable<Curve>> subtractors, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curveA, subtractors, tolerance);
        }

        /// <summary>
        /// Curve Boolean method, which trims and splits curves based on their overlapping regions.
        /// </summary>
        /// <param name="curves">The input curves.</param>
        /// <param name="plane">Regions will be found in the projection of the curves to this plane.</param>
        /// <param name="points">These points will be projected to plane. All regions that contain at least one of these points will be found.</param>
        /// <param name="combineRegions">If true, then adjacent regions will be combined.</param>
        /// <param name="tolerance">Function tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>The curve Boolean regions if successful, null of no successful.</returns>
        public static CurveBooleanRegions CreateBooleanRegions(IEnumerable<Curve> curves, Plane plane, IEnumerable<Point3d> points, bool combineRegions, double tolerance)
        {
            return ComputeServer.Post<CurveBooleanRegions>(ApiAddress(), curves, plane, points, combineRegions, tolerance);
        }

        /// <summary>
        /// Curve Boolean method, which trims and splits curves based on their overlapping regions.
        /// </summary>
        /// <param name="curves">The input curves.</param>
        /// <param name="plane">Regions will be found in the projection of the curves to this plane.</param>
        /// <param name="points">These points will be projected to plane. All regions that contain at least one of these points will be found.</param>
        /// <param name="combineRegions">If true, then adjacent regions will be combined.</param>
        /// <param name="tolerance">Function tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>The curve Boolean regions if successful, null of no successful.</returns>
        public static CurveBooleanRegions CreateBooleanRegions(Remote<IEnumerable<Curve>> curves, Plane plane, IEnumerable<Point3d> points, bool combineRegions, double tolerance)
        {
            return ComputeServer.Post<CurveBooleanRegions>(ApiAddress(), curves, plane, points, combineRegions, tolerance);
        }

        /// <summary>
        /// Calculates curve Boolean regions, which trims and splits curves based on their overlapping regions.
        /// </summary>
        /// <param name="curves">The input curves.</param>
        /// <param name="plane">Regions will be found in the projection of the curves to this plane.</param>
        /// <param name="combineRegions">If true, then adjacent regions will be combined.</param>
        /// <param name="tolerance">Function tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>The curve Boolean regions if successful, null of no successful.</returns>
        public static CurveBooleanRegions CreateBooleanRegions(IEnumerable<Curve> curves, Plane plane, bool combineRegions, double tolerance)
        {
            return ComputeServer.Post<CurveBooleanRegions>(ApiAddress(), curves, plane, combineRegions, tolerance);
        }

        /// <summary>
        /// Calculates curve Boolean regions, which trims and splits curves based on their overlapping regions.
        /// </summary>
        /// <param name="curves">The input curves.</param>
        /// <param name="plane">Regions will be found in the projection of the curves to this plane.</param>
        /// <param name="combineRegions">If true, then adjacent regions will be combined.</param>
        /// <param name="tolerance">Function tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>The curve Boolean regions if successful, null of no successful.</returns>
        public static CurveBooleanRegions CreateBooleanRegions(Remote<IEnumerable<Curve>> curves, Plane plane, bool combineRegions, double tolerance)
        {
            return ComputeServer.Post<CurveBooleanRegions>(ApiAddress(), curves, plane, combineRegions, tolerance);
        }

        /// <summary>
        /// Creates outline curves created from a text string. The functionality is similar to what you find in Rhino's TextObject command or TextEntity.Explode() in RhinoCommon.
        /// </summary>
        /// <param name="text">The text from which to create outline curves.</param>
        /// <param name="font">The text font.</param>
        /// <param name="textHeight">The text height.</param>
        /// <param name="textStyle">The font style. The font style can be any number of the following: 0 - Normal, 1 - Bold, 2 - Italic</param>
        /// <param name="closeLoops">Set this value to True when dealing with normal fonts and when you expect closed loops. You may want to set this to False when specifying a single-stroke font where you don't want closed loops.</param>
        /// <param name="plane">The plane on which the outline curves will lie.</param>
        /// <param name="smallCapsScale">Displays lower-case letters as small caps. Set the relative text size to a percentage of the normal text.</param>
        /// <param name="tolerance">The tolerance for the operation.</param>
        /// <returns>An array containing one or more curves if successful.</returns>
        public static Curve[] CreateTextOutlines(string text, string font, double textHeight, int textStyle, bool closeLoops, Plane plane, double smallCapsScale, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), text, font, textHeight, textStyle, closeLoops, plane, smallCapsScale, tolerance);
        }

        /// <summary>
        /// Creates a third curve from two curves that are planar in different construction planes. 
        /// The new curve looks the same as each of the original curves when viewed in each plane.
        /// </summary>
        /// <param name="curveA">The first curve.</param>
        /// <param name="curveB">The second curve.</param>
        /// <param name="vectorA">A vector defining the normal direction of the plane which the first curve is drawn upon.</param>
        /// <param name="vectorB">A vector defining the normal direction of the plane which the second curve is drawn upon.</param>
        /// <param name="tolerance">The tolerance for the operation.</param>
        /// <param name="angleTolerance">The angle tolerance for the operation.</param>
        /// <returns>An array containing one or more curves if successful.</returns>
        public static Curve[] CreateCurve2View(Curve curveA, Curve curveB, Vector3d vectorA, Vector3d vectorB, double tolerance, double angleTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curveA, curveB, vectorA, vectorB, tolerance, angleTolerance);
        }

        /// <summary>
        /// Creates a third curve from two curves that are planar in different construction planes. 
        /// The new curve looks the same as each of the original curves when viewed in each plane.
        /// </summary>
        /// <param name="curveA">The first curve.</param>
        /// <param name="curveB">The second curve.</param>
        /// <param name="vectorA">A vector defining the normal direction of the plane which the first curve is drawn upon.</param>
        /// <param name="vectorB">A vector defining the normal direction of the plane which the second curve is drawn upon.</param>
        /// <param name="tolerance">The tolerance for the operation.</param>
        /// <param name="angleTolerance">The angle tolerance for the operation.</param>
        /// <returns>An array containing one or more curves if successful.</returns>
        public static Curve[] CreateCurve2View(Remote<Curve> curveA, Remote<Curve> curveB, Vector3d vectorA, Vector3d vectorB, double tolerance, double angleTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curveA, curveB, vectorA, vectorB, tolerance, angleTolerance);
        }

        /// <summary>
        /// Determines whether two curves travel more or less in the same direction.
        /// </summary>
        /// <param name="curveA">First curve to test.</param>
        /// <param name="curveB">Second curve to test.</param>
        /// <returns>true if both curves more or less point in the same direction, 
        /// false if they point in the opposite directions.</returns>
        public static bool DoDirectionsMatch(Curve curveA, Curve curveB)
        {
            return ComputeServer.Post<bool>(ApiAddress(), curveA, curveB);
        }

        /// <summary>
        /// Determines whether two curves travel more or less in the same direction.
        /// </summary>
        /// <param name="curveA">First curve to test.</param>
        /// <param name="curveB">Second curve to test.</param>
        /// <returns>true if both curves more or less point in the same direction, 
        /// false if they point in the opposite directions.</returns>
        public static bool DoDirectionsMatch(Remote<Curve> curveA, Remote<Curve> curveB)
        {
            return ComputeServer.Post<bool>(ApiAddress(), curveA, curveB);
        }

        /// <summary>
        /// Projects a curve to a mesh using a direction and tolerance.
        /// </summary>
        /// <param name="curve">A curve.</param>
        /// <param name="mesh">A mesh.</param>
        /// <param name="direction">A direction vector.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>A curve array.</returns>
        public static Curve[] ProjectToMesh(Curve curve, Mesh mesh, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, mesh, direction, tolerance);
        }

        /// <summary>
        /// Projects a curve to a mesh using a direction and tolerance.
        /// </summary>
        /// <param name="curve">A curve.</param>
        /// <param name="mesh">A mesh.</param>
        /// <param name="direction">A direction vector.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>A curve array.</returns>
        public static Curve[] ProjectToMesh(Remote<Curve> curve, Remote<Mesh> mesh, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, mesh, direction, tolerance);
        }

        /// <summary>
        /// Projects a curve to a set of meshes using a direction and tolerance.
        /// </summary>
        /// <param name="curve">A curve.</param>
        /// <param name="meshes">A list, an array or any enumerable of meshes.</param>
        /// <param name="direction">A direction vector.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>A curve array.</returns>
        public static Curve[] ProjectToMesh(Curve curve, IEnumerable<Mesh> meshes, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, meshes, direction, tolerance);
        }

        /// <summary>
        /// Projects a curve to a set of meshes using a direction and tolerance.
        /// </summary>
        /// <param name="curve">A curve.</param>
        /// <param name="meshes">A list, an array or any enumerable of meshes.</param>
        /// <param name="direction">A direction vector.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>A curve array.</returns>
        public static Curve[] ProjectToMesh(Remote<Curve> curve, Remote<IEnumerable<Mesh>> meshes, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, meshes, direction, tolerance);
        }

        /// <summary>
        /// Projects a curve to a set of meshes using a direction and tolerance.
        /// </summary>
        /// <param name="curves">A list, an array or any enumerable of curves.</param>
        /// <param name="meshes">A list, an array or any enumerable of meshes.</param>
        /// <param name="direction">A direction vector.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>A curve array.</returns>
        public static Curve[] ProjectToMesh(IEnumerable<Curve> curves, IEnumerable<Mesh> meshes, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curves, meshes, direction, tolerance);
        }

        /// <summary>
        /// Projects a curve to a set of meshes using a direction and tolerance.
        /// </summary>
        /// <param name="curves">A list, an array or any enumerable of curves.</param>
        /// <param name="meshes">A list, an array or any enumerable of meshes.</param>
        /// <param name="direction">A direction vector.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>A curve array.</returns>
        public static Curve[] ProjectToMesh(Remote<IEnumerable<Curve>> curves, Remote<IEnumerable<Mesh>> meshes, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curves, meshes, direction, tolerance);
        }

        /// <summary>
        /// Projects a Curve onto a Brep along a given direction.
        /// </summary>
        /// <param name="curve">Curve to project.</param>
        /// <param name="brep">Brep to project onto.</param>
        /// <param name="direction">Direction of projection.</param>
        /// <param name="tolerance">Tolerance to use for projection.</param>
        /// <returns>An array of projected curves or empty array if the projection set is empty.</returns>
        public static Curve[] ProjectToBrep(Curve curve, Brep brep, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, brep, direction, tolerance);
        }

        /// <summary>
        /// Projects a Curve onto a Brep along a given direction.
        /// </summary>
        /// <param name="curve">Curve to project.</param>
        /// <param name="brep">Brep to project onto.</param>
        /// <param name="direction">Direction of projection.</param>
        /// <param name="tolerance">Tolerance to use for projection.</param>
        /// <returns>An array of projected curves or empty array if the projection set is empty.</returns>
        public static Curve[] ProjectToBrep(Remote<Curve> curve, Remote<Brep> brep, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, brep, direction, tolerance);
        }

        /// <summary>
        /// Projects a Curve onto a collection of Breps along a given direction.
        /// </summary>
        /// <param name="curve">Curve to project.</param>
        /// <param name="breps">Breps to project onto.</param>
        /// <param name="direction">Direction of projection.</param>
        /// <param name="tolerance">Tolerance to use for projection.</param>
        /// <returns>An array of projected curves or empty array if the projection set is empty.</returns>
        public static Curve[] ProjectToBrep(Curve curve, IEnumerable<Brep> breps, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, breps, direction, tolerance);
        }

        /// <summary>
        /// Projects a Curve onto a collection of Breps along a given direction.
        /// </summary>
        /// <param name="curve">Curve to project.</param>
        /// <param name="breps">Breps to project onto.</param>
        /// <param name="direction">Direction of projection.</param>
        /// <param name="tolerance">Tolerance to use for projection.</param>
        /// <returns>An array of projected curves or empty array if the projection set is empty.</returns>
        public static Curve[] ProjectToBrep(Remote<Curve> curve, Remote<IEnumerable<Brep>> breps, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, breps, direction, tolerance);
        }

        /// <summary>
        /// Projects a Curve onto a collection of Breps along a given direction.
        /// </summary>
        /// <param name="curve">Curve to project.</param>
        /// <param name="breps">Breps to project onto.</param>
        /// <param name="direction">Direction of projection.</param>
        /// <param name="tolerance">Tolerance to use for projection.</param>
        /// <param name="brepIndices">(out) Integers that identify for each resulting curve which Brep it was projected onto.</param>
        /// <returns>An array of projected curves or null if the projection set is empty.</returns>
        public static Curve[] ProjectToBrep(Curve curve, IEnumerable<Brep> breps, Vector3d direction, double tolerance, out int[] brepIndices)
        {
            return ComputeServer.Post<Curve[], int[]>(ApiAddress(), out brepIndices, curve, breps, direction, tolerance);
        }

        /// <summary>
        /// Projects a Curve onto a collection of Breps along a given direction.
        /// </summary>
        /// <param name="curve">Curve to project.</param>
        /// <param name="breps">Breps to project onto.</param>
        /// <param name="direction">Direction of projection.</param>
        /// <param name="tolerance">Tolerance to use for projection.</param>
        /// <param name="brepIndices">(out) Integers that identify for each resulting curve which Brep it was projected onto.</param>
        /// <returns>An array of projected curves or null if the projection set is empty.</returns>
        public static Curve[] ProjectToBrep(Remote<Curve> curve, Remote<IEnumerable<Brep>> breps, Vector3d direction, double tolerance, out int[] brepIndices)
        {
            return ComputeServer.Post<Curve[], int[]>(ApiAddress(), out brepIndices, curve, breps, direction, tolerance);
        }

        /// <summary>
        /// Projects a collection of Curves onto a collection of Breps along a given direction.
        /// </summary>
        /// <param name="curves">Curves to project.</param>
        /// <param name="breps">Breps to project onto.</param>
        /// <param name="direction">Direction of projection.</param>
        /// <param name="tolerance">Tolerance to use for projection.</param>
        /// <returns>An array of projected curves or empty array if the projection set is empty.</returns>
        public static Curve[] ProjectToBrep(IEnumerable<Curve> curves, IEnumerable<Brep> breps, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curves, breps, direction, tolerance);
        }

        /// <summary>
        /// Projects a collection of Curves onto a collection of Breps along a given direction.
        /// </summary>
        /// <param name="curves">Curves to project.</param>
        /// <param name="breps">Breps to project onto.</param>
        /// <param name="direction">Direction of projection.</param>
        /// <param name="tolerance">Tolerance to use for projection.</param>
        /// <returns>An array of projected curves or empty array if the projection set is empty.</returns>
        public static Curve[] ProjectToBrep(Remote<IEnumerable<Curve>> curves, Remote<IEnumerable<Brep>> breps, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curves, breps, direction, tolerance);
        }

        /// <summary>
        /// Projects a collection of Curves onto a collection of Breps along a given direction.
        /// </summary>
        /// <param name="curves">Curves to project.</param>
        /// <param name="breps">Breps to project onto.</param>
        /// <param name="direction">Direction of projection.</param>
        /// <param name="tolerance">Tolerance to use for projection.</param>
        /// <param name="curveIndices">Index of which curve in the input list was the source for a curve in the return array.</param>
        /// <param name="brepIndices">Index of which brep was used to generate a curve in the return array.</param>
        /// <returns>An array of projected curves. Array is empty if the projection set is empty.</returns>
        public static Curve[] ProjectToBrep(IEnumerable<Curve> curves, IEnumerable<Brep> breps, Vector3d direction, double tolerance, out int[] curveIndices, out int[] brepIndices)
        {
            return ComputeServer.Post<Curve[], int[], int[]>(ApiAddress(), out curveIndices, out brepIndices, curves, breps, direction, tolerance);
        }

        /// <summary>
        /// Projects a collection of Curves onto a collection of Breps along a given direction.
        /// </summary>
        /// <param name="curves">Curves to project.</param>
        /// <param name="breps">Breps to project onto.</param>
        /// <param name="direction">Direction of projection.</param>
        /// <param name="tolerance">Tolerance to use for projection.</param>
        /// <param name="curveIndices">Index of which curve in the input list was the source for a curve in the return array.</param>
        /// <param name="brepIndices">Index of which brep was used to generate a curve in the return array.</param>
        /// <returns>An array of projected curves. Array is empty if the projection set is empty.</returns>
        public static Curve[] ProjectToBrep(Remote<IEnumerable<Curve>> curves, Remote<IEnumerable<Brep>> breps, Vector3d direction, double tolerance, out int[] curveIndices, out int[] brepIndices)
        {
            return ComputeServer.Post<Curve[], int[], int[]>(ApiAddress(), out curveIndices, out brepIndices, curves, breps, direction, tolerance);
        }

        /// <summary>
        /// Constructs a curve by projecting an existing curve to a plane.
        /// </summary>
        /// <param name="curve">A curve.</param>
        /// <param name="plane">A plane.</param>
        /// <returns>The projected curve on success; null on failure.</returns>
        public static Curve ProjectToPlane(Curve curve, Plane plane)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, plane);
        }

        /// <summary>
        /// Constructs a curve by projecting an existing curve to a plane.
        /// </summary>
        /// <param name="curve">A curve.</param>
        /// <param name="plane">A plane.</param>
        /// <returns>The projected curve on success; null on failure.</returns>
        public static Curve ProjectToPlane(Remote<Curve> curve, Plane plane)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, plane);
        }

        /// <summary>
        /// Pull a curve to a BrepFace using closest point projection.
        /// </summary>
        /// <param name="curve">Curve to pull.</param>
        /// <param name="face">Brep face that pulls.</param>
        /// <param name="tolerance">Tolerance to use for pulling.</param>
        /// <returns>An array of pulled curves, or an empty array on failure.</returns>
        public static Curve[] PullToBrepFace(Curve curve, BrepFace face, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, face, tolerance);
        }

        /// <summary>
        /// Pull a curve to a BrepFace using closest point projection.
        /// </summary>
        /// <param name="curve">Curve to pull.</param>
        /// <param name="face">Brep face that pulls.</param>
        /// <param name="tolerance">Tolerance to use for pulling.</param>
        /// <returns>An array of pulled curves, or an empty array on failure.</returns>
        public static Curve[] PullToBrepFace(Remote<Curve> curve, BrepFace face, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, face, tolerance);
        }

        /// <summary>
        /// Determines whether two coplanar simple closed curves are disjoint or intersect;
        /// otherwise, if the regions have a containment relationship, discovers
        /// which curve encloses the other.
        /// </summary>
        /// <param name="curveA">A first curve.</param>
        /// <param name="curveB">A second curve.</param>
        /// <param name="testPlane">A plane.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>
        /// A value indicating the relationship between the first and the second curve.
        /// </returns>
        public static RegionContainment PlanarClosedCurveRelationship(Curve curveA, Curve curveB, Plane testPlane, double tolerance)
        {
            return ComputeServer.Post<RegionContainment>(ApiAddress(), curveA, curveB, testPlane, tolerance);
        }

        /// <summary>
        /// Determines whether two coplanar simple closed curves are disjoint or intersect;
        /// otherwise, if the regions have a containment relationship, discovers
        /// which curve encloses the other.
        /// </summary>
        /// <param name="curveA">A first curve.</param>
        /// <param name="curveB">A second curve.</param>
        /// <param name="testPlane">A plane.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>
        /// A value indicating the relationship between the first and the second curve.
        /// </returns>
        public static RegionContainment PlanarClosedCurveRelationship(Remote<Curve> curveA, Remote<Curve> curveB, Plane testPlane, double tolerance)
        {
            return ComputeServer.Post<RegionContainment>(ApiAddress(), curveA, curveB, testPlane, tolerance);
        }

        /// <summary>
        /// Determines if two coplanar curves collide (intersect).
        /// </summary>
        /// <param name="curveA">A curve.</param>
        /// <param name="curveB">Another curve.</param>
        /// <param name="testPlane">A valid plane containing the curves.</param>
        /// <param name="tolerance">A tolerance value for intersection.</param>
        /// <returns>true if the curves intersect, otherwise false</returns>
        public static bool PlanarCurveCollision(Curve curveA, Curve curveB, Plane testPlane, double tolerance)
        {
            return ComputeServer.Post<bool>(ApiAddress(), curveA, curveB, testPlane, tolerance);
        }

        /// <summary>
        /// Determines if two coplanar curves collide (intersect).
        /// </summary>
        /// <param name="curveA">A curve.</param>
        /// <param name="curveB">Another curve.</param>
        /// <param name="testPlane">A valid plane containing the curves.</param>
        /// <param name="tolerance">A tolerance value for intersection.</param>
        /// <returns>true if the curves intersect, otherwise false</returns>
        public static bool PlanarCurveCollision(Remote<Curve> curveA, Remote<Curve> curveB, Plane testPlane, double tolerance)
        {
            return ComputeServer.Post<bool>(ApiAddress(), curveA, curveB, testPlane, tolerance);
        }

        /// <summary>
        /// Polylines will be exploded into line segments. ExplodeCurves will
        /// return the curves in topological order.
        /// </summary>
        /// <returns>
        /// An array of all the segments that make up this curve.
        /// </returns>
        public static Curve[] DuplicateSegments(this Curve curve, out Curve updatedInstance)
        {
            return ComputeServer.Post<Curve[], Curve>(ApiAddress(), out updatedInstance, curve);
        }

        /// <summary>
        /// Polylines will be exploded into line segments. ExplodeCurves will
        /// return the curves in topological order.
        /// </summary>
        /// <returns>
        /// An array of all the segments that make up this curve.
        /// </returns>
        public static Curve[] DuplicateSegments(Remote<Curve> curve, out Curve updatedInstance)
        {
            return ComputeServer.Post<Curve[], Curve>(ApiAddress(), out updatedInstance, curve);
        }

        /// <summary>
        /// Smooths a curve by averaging the positions of control points in a specified region.
        /// </summary>
        /// <param name="smoothFactor">The smoothing factor, which controls how much control points move towards the average of the neighboring control points.</param>
        /// <param name="bXSmooth">When true control points move in X axis direction.</param>
        /// <param name="bYSmooth">When true control points move in Y axis direction.</param>
        /// <param name="bZSmooth">When true control points move in Z axis direction.</param>
        /// <param name="bFixBoundaries">When true the curve ends don't move.</param>
        /// <param name="coordinateSystem">The coordinates to determine the direction of the smoothing.</param>
        /// <returns>The smoothed curve if successful, null otherwise.</returns>
        public static Curve Smooth(this Curve curve, out Curve updatedInstance, double smoothFactor, bool bXSmooth, bool bYSmooth, bool bZSmooth, bool bFixBoundaries, SmoothingCoordinateSystem coordinateSystem)
        {
            return ComputeServer.Post<Curve, Curve>(ApiAddress(), out updatedInstance, curve, smoothFactor, bXSmooth, bYSmooth, bZSmooth, bFixBoundaries, coordinateSystem);
        }

        /// <summary>
        /// Smooths a curve by averaging the positions of control points in a specified region.
        /// </summary>
        /// <param name="smoothFactor">The smoothing factor, which controls how much control points move towards the average of the neighboring control points.</param>
        /// <param name="bXSmooth">When true control points move in X axis direction.</param>
        /// <param name="bYSmooth">When true control points move in Y axis direction.</param>
        /// <param name="bZSmooth">When true control points move in Z axis direction.</param>
        /// <param name="bFixBoundaries">When true the curve ends don't move.</param>
        /// <param name="coordinateSystem">The coordinates to determine the direction of the smoothing.</param>
        /// <returns>The smoothed curve if successful, null otherwise.</returns>
        public static Curve Smooth(Remote<Curve> curve, out Curve updatedInstance, double smoothFactor, bool bXSmooth, bool bYSmooth, bool bZSmooth, bool bFixBoundaries, SmoothingCoordinateSystem coordinateSystem)
        {
            return ComputeServer.Post<Curve, Curve>(ApiAddress(), out updatedInstance, curve, smoothFactor, bXSmooth, bYSmooth, bZSmooth, bFixBoundaries, coordinateSystem);
        }

        /// <summary>
        /// Smooths a curve by averaging the positions of control points in a specified region.
        /// </summary>
        /// <param name="smoothFactor">The smoothing factor, which controls how much control points move towards the average of the neighboring control points.</param>
        /// <param name="bXSmooth">When true control points move in X axis direction.</param>
        /// <param name="bYSmooth">When true control points move in Y axis direction.</param>
        /// <param name="bZSmooth">When true control points move in Z axis direction.</param>
        /// <param name="bFixBoundaries">When true the curve ends don't move.</param>
        /// <param name="coordinateSystem">The coordinates to determine the direction of the smoothing.</param>
        /// <param name="plane">If SmoothingCoordinateSystem.CPlane specified, then the construction plane.</param>
        /// <returns>The smoothed curve if successful, null otherwise.</returns>
        public static Curve Smooth(this Curve curve, out Curve updatedInstance, double smoothFactor, bool bXSmooth, bool bYSmooth, bool bZSmooth, bool bFixBoundaries, SmoothingCoordinateSystem coordinateSystem, Plane plane)
        {
            return ComputeServer.Post<Curve, Curve>(ApiAddress(), out updatedInstance, curve, smoothFactor, bXSmooth, bYSmooth, bZSmooth, bFixBoundaries, coordinateSystem, plane);
        }

        /// <summary>
        /// Smooths a curve by averaging the positions of control points in a specified region.
        /// </summary>
        /// <param name="smoothFactor">The smoothing factor, which controls how much control points move towards the average of the neighboring control points.</param>
        /// <param name="bXSmooth">When true control points move in X axis direction.</param>
        /// <param name="bYSmooth">When true control points move in Y axis direction.</param>
        /// <param name="bZSmooth">When true control points move in Z axis direction.</param>
        /// <param name="bFixBoundaries">When true the curve ends don't move.</param>
        /// <param name="coordinateSystem">The coordinates to determine the direction of the smoothing.</param>
        /// <param name="plane">If SmoothingCoordinateSystem.CPlane specified, then the construction plane.</param>
        /// <returns>The smoothed curve if successful, null otherwise.</returns>
        public static Curve Smooth(Remote<Curve> curve, out Curve updatedInstance, double smoothFactor, bool bXSmooth, bool bYSmooth, bool bZSmooth, bool bFixBoundaries, SmoothingCoordinateSystem coordinateSystem, Plane plane)
        {
            return ComputeServer.Post<Curve, Curve>(ApiAddress(), out updatedInstance, curve, smoothFactor, bXSmooth, bYSmooth, bZSmooth, bFixBoundaries, coordinateSystem, plane);
        }

        /// <summary>
        /// Search for a location on the curve, near seedParmameter, that is perpendicular to a test point.
        /// </summary>
        /// <param name="testPoint">The test point.</param>
        /// <param name="seedParmameter">A "seed" parameter on the curve.</param>
        /// <param name="curveParameter">The parameter value at the perpendicular point</param>
        /// <returns>True if a solution is found, false otherwise.</returns>
        public static bool GetLocalPerpPoint(this Curve curve, Point3d testPoint, double seedParmameter, out double curveParameter)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out curveParameter, curve, testPoint, seedParmameter);
        }

        /// <summary>
        /// Search for a location on the curve, near seedParmameter, that is perpendicular to a test point.
        /// </summary>
        /// <param name="testPoint">The test point.</param>
        /// <param name="seedParmameter">A "seed" parameter on the curve.</param>
        /// <param name="curveParameter">The parameter value at the perpendicular point</param>
        /// <returns>True if a solution is found, false otherwise.</returns>
        public static bool GetLocalPerpPoint(Remote<Curve> curve, Point3d testPoint, double seedParmameter, out double curveParameter)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out curveParameter, curve, testPoint, seedParmameter);
        }

        /// <summary>
        /// Search for a location on the curve, near seedParmameter, that is perpendicular to a test point.
        /// </summary>
        /// <param name="testPoint">The test point.</param>
        /// <param name="seedParmameter">A "seed" parameter on the curve.</param>
        /// <param name="subDomain">The sub-domain of the curve to search.</param>
        /// <param name="curveParameter">The parameter value at the perpendicular point</param>
        /// <returns>True if a solution is found, false otherwise.</returns>
        public static bool GetLocalPerpPoint(this Curve curve, Point3d testPoint, double seedParmameter, Interval subDomain, out double curveParameter)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out curveParameter, curve, testPoint, seedParmameter, subDomain);
        }

        /// <summary>
        /// Search for a location on the curve, near seedParmameter, that is perpendicular to a test point.
        /// </summary>
        /// <param name="testPoint">The test point.</param>
        /// <param name="seedParmameter">A "seed" parameter on the curve.</param>
        /// <param name="subDomain">The sub-domain of the curve to search.</param>
        /// <param name="curveParameter">The parameter value at the perpendicular point</param>
        /// <returns>True if a solution is found, false otherwise.</returns>
        public static bool GetLocalPerpPoint(Remote<Curve> curve, Point3d testPoint, double seedParmameter, Interval subDomain, out double curveParameter)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out curveParameter, curve, testPoint, seedParmameter, subDomain);
        }

        /// <summary>
        /// Search for a location on the curve, near seedParmameter, that is tangent to a test point.
        /// </summary>
        /// <param name="testPoint">The test point.</param>
        /// <param name="seedParmameter">A "seed" parameter on the curve.</param>
        /// <param name="curveParameter">The parameter value at the tangent point</param>
        /// <returns>True if a solution is found, false otherwise.</returns>
        public static bool GetLocalTangentPoint(this Curve curve, Point3d testPoint, double seedParmameter, out double curveParameter)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out curveParameter, curve, testPoint, seedParmameter);
        }

        /// <summary>
        /// Search for a location on the curve, near seedParmameter, that is tangent to a test point.
        /// </summary>
        /// <param name="testPoint">The test point.</param>
        /// <param name="seedParmameter">A "seed" parameter on the curve.</param>
        /// <param name="curveParameter">The parameter value at the tangent point</param>
        /// <returns>True if a solution is found, false otherwise.</returns>
        public static bool GetLocalTangentPoint(Remote<Curve> curve, Point3d testPoint, double seedParmameter, out double curveParameter)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out curveParameter, curve, testPoint, seedParmameter);
        }

        /// <summary>
        /// Search for a location on the curve, near seedParmameter, that is tangent to a test point.
        /// </summary>
        /// <param name="testPoint">The test point.</param>
        /// <param name="seedParmameter">A "seed" parameter on the curve.</param>
        /// <param name="subDomain">The sub-domain of the curve to search.</param>
        /// <param name="curveParameter">The parameter value at the tangent point</param>
        /// <returns>True if a solution is found, false otherwise.</returns>
        public static bool GetLocalTangentPoint(this Curve curve, Point3d testPoint, double seedParmameter, Interval subDomain, out double curveParameter)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out curveParameter, curve, testPoint, seedParmameter, subDomain);
        }

        /// <summary>
        /// Search for a location on the curve, near seedParmameter, that is tangent to a test point.
        /// </summary>
        /// <param name="testPoint">The test point.</param>
        /// <param name="seedParmameter">A "seed" parameter on the curve.</param>
        /// <param name="subDomain">The sub-domain of the curve to search.</param>
        /// <param name="curveParameter">The parameter value at the tangent point</param>
        /// <returns>True if a solution is found, false otherwise.</returns>
        public static bool GetLocalTangentPoint(Remote<Curve> curve, Point3d testPoint, double seedParmameter, Interval subDomain, out double curveParameter)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out curveParameter, curve, testPoint, seedParmameter, subDomain);
        }

        /// <summary>
        /// Returns a curve's inflection points. An inflection point is a location on
        /// a curve at which the sign of the curvature (i.e., the concavity) changes. 
        /// The curvature at these locations is always 0.
        /// </summary>
        /// <returns>An array of points if successful, null if not successful or on error.</returns>
        public static Point3d[] InflectionPoints(this Curve curve)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), curve);
        }

        /// <summary>
        /// Returns a curve's inflection points. An inflection point is a location on
        /// a curve at which the sign of the curvature (i.e., the concavity) changes. 
        /// The curvature at these locations is always 0.
        /// </summary>
        /// <returns>An array of points if successful, null if not successful or on error.</returns>
        public static Point3d[] InflectionPoints(Remote<Curve> curve)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), curve);
        }

        /// <summary>
        /// Returns a curve's maximum curvature points. The maximum curvature points identify
        /// where the curvature starts to decrease in both directions from the points.
        /// </summary>
        /// <returns>An array of points if successful, null if not successful or on error.</returns>
        public static Point3d[] MaxCurvaturePoints(this Curve curve)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), curve);
        }

        /// <summary>
        /// Returns a curve's maximum curvature points. The maximum curvature points identify
        /// where the curvature starts to decrease in both directions from the points.
        /// </summary>
        /// <returns>An array of points if successful, null if not successful or on error.</returns>
        public static Point3d[] MaxCurvaturePoints(Remote<Curve> curve)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), curve);
        }

        /// <summary>
        /// If IsClosed, just return true. Otherwise, decide if curve can be closed as 
        /// follows: Linear curves polylinear curves with 2 segments, NURBS with 3 or less 
        /// control points cannot be made closed. Also, if tolerance > 0 and the gap between 
        /// start and end is larger than tolerance, curve cannot be made closed. 
        /// Adjust the curve's endpoint to match its start point.
        /// </summary>
        /// <param name="tolerance">
        /// If nonzero, and the gap is more than tolerance, curve cannot be made closed.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool MakeClosed(this Curve curve, out Curve updatedInstance, double tolerance)
        {
            return ComputeServer.Post<bool, Curve>(ApiAddress(), out updatedInstance, curve, tolerance);
        }

        /// <summary>
        /// If IsClosed, just return true. Otherwise, decide if curve can be closed as 
        /// follows: Linear curves polylinear curves with 2 segments, NURBS with 3 or less 
        /// control points cannot be made closed. Also, if tolerance > 0 and the gap between 
        /// start and end is larger than tolerance, curve cannot be made closed. 
        /// Adjust the curve's endpoint to match its start point.
        /// </summary>
        /// <param name="tolerance">
        /// If nonzero, and the gap is more than tolerance, curve cannot be made closed.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool MakeClosed(Remote<Curve> curve, out Curve updatedInstance, double tolerance)
        {
            return ComputeServer.Post<bool, Curve>(ApiAddress(), out updatedInstance, curve, tolerance);
        }

        /// <summary>
        /// Find parameter of the point on a curve that is locally closest to 
        /// the testPoint.  The search for a local close point starts at
        /// a seed parameter.
        /// </summary>
        /// <param name="testPoint">A point to test against.</param>
        /// <param name="seed">The seed parameter.</param>
        /// <param name="t">>Parameter of the curve that is closest to testPoint.</param>
        /// <returns>true if the search is successful, false if the search fails.</returns>
        /// <deprecated>6.18</deprecated>
        public static bool LcoalClosestPoint(this Curve curve, Point3d testPoint, double seed, out double t)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, testPoint, seed);
        }

        /// <summary>
        /// Find parameter of the point on a curve that is locally closest to 
        /// the testPoint.  The search for a local close point starts at
        /// a seed parameter.
        /// </summary>
        /// <param name="testPoint">A point to test against.</param>
        /// <param name="seed">The seed parameter.</param>
        /// <param name="t">>Parameter of the curve that is closest to testPoint.</param>
        /// <returns>true if the search is successful, false if the search fails.</returns>
        /// <deprecated>6.18</deprecated>
        public static bool LcoalClosestPoint(Remote<Curve> curve, Point3d testPoint, double seed, out double t)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, testPoint, seed);
        }

        /// <summary>
        /// Find parameter of the point on a curve that is locally closest to 
        /// the testPoint.  The search for a local close point starts at
        /// a seed parameter.
        /// </summary>
        /// <param name="testPoint">A point to test against.</param>
        /// <param name="seed">The seed parameter.</param>
        /// <param name="t">>Parameter of the curve that is closest to testPoint.</param>
        /// <returns>true if the search is successful, false if the search fails.</returns>
        public static bool LocalClosestPoint(this Curve curve, Point3d testPoint, double seed, out double t)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, testPoint, seed);
        }

        /// <summary>
        /// Find parameter of the point on a curve that is locally closest to 
        /// the testPoint.  The search for a local close point starts at
        /// a seed parameter.
        /// </summary>
        /// <param name="testPoint">A point to test against.</param>
        /// <param name="seed">The seed parameter.</param>
        /// <param name="t">>Parameter of the curve that is closest to testPoint.</param>
        /// <returns>true if the search is successful, false if the search fails.</returns>
        public static bool LocalClosestPoint(Remote<Curve> curve, Point3d testPoint, double seed, out double t)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, testPoint, seed);
        }

        /// <summary>
        /// Finds parameter of the point on a curve that is closest to testPoint.
        /// If the maximumDistance parameter is > 0, then only points whose distance
        /// to the given point is &lt;= maximumDistance will be returned.  Using a 
        /// positive value of maximumDistance can substantially speed up the search.
        /// </summary>
        /// <param name="testPoint">Point to search from.</param>
        /// <param name="t">Parameter of local closest point.</param>
        /// <returns>true on success, false on failure.</returns>
        public static bool ClosestPoint(this Curve curve, Point3d testPoint, out double t)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, testPoint);
        }

        /// <summary>
        /// Finds parameter of the point on a curve that is closest to testPoint.
        /// If the maximumDistance parameter is > 0, then only points whose distance
        /// to the given point is &lt;= maximumDistance will be returned.  Using a 
        /// positive value of maximumDistance can substantially speed up the search.
        /// </summary>
        /// <param name="testPoint">Point to search from.</param>
        /// <param name="t">Parameter of local closest point.</param>
        /// <returns>true on success, false on failure.</returns>
        public static bool ClosestPoint(Remote<Curve> curve, Point3d testPoint, out double t)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, testPoint);
        }

        /// <summary>
        /// Finds the parameter of the point on a curve that is closest to testPoint.
        /// If the maximumDistance parameter is > 0, then only points whose distance
        /// to the given point is &lt;= maximumDistance will be returned.  Using a 
        /// positive value of maximumDistance can substantially speed up the search.
        /// </summary>
        /// <param name="testPoint">Point to project.</param>
        /// <param name="t">parameter of local closest point returned here.</param>
        /// <param name="maximumDistance">The maximum allowed distance.
        /// <para>Past this distance, the search is given up and false is returned.</para>
        /// <para>Use 0 to turn off this parameter.</para></param>
        /// <returns>true on success, false on failure.</returns>
        public static bool ClosestPoint(this Curve curve, Point3d testPoint, out double t, double maximumDistance)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, testPoint, maximumDistance);
        }

        /// <summary>
        /// Finds the parameter of the point on a curve that is closest to testPoint.
        /// If the maximumDistance parameter is > 0, then only points whose distance
        /// to the given point is &lt;= maximumDistance will be returned.  Using a 
        /// positive value of maximumDistance can substantially speed up the search.
        /// </summary>
        /// <param name="testPoint">Point to project.</param>
        /// <param name="t">parameter of local closest point returned here.</param>
        /// <param name="maximumDistance">The maximum allowed distance.
        /// <para>Past this distance, the search is given up and false is returned.</para>
        /// <para>Use 0 to turn off this parameter.</para></param>
        /// <returns>true on success, false on failure.</returns>
        public static bool ClosestPoint(Remote<Curve> curve, Point3d testPoint, out double t, double maximumDistance)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, testPoint, maximumDistance);
        }

        /// <summary>
        /// Gets closest points between this and another curves.
        /// </summary>
        /// <param name="otherCurve">The other curve.</param>
        /// <param name="pointOnThisCurve">The point on this curve. This out parameter is assigned during this call.</param>
        /// <param name="pointOnOtherCurve">The point on other curve. This out parameter is assigned during this call.</param>
        /// <returns>true on success; false on error.</returns>
        public static bool ClosestPoints(this Curve curve, Curve otherCurve, out Point3d pointOnThisCurve, out Point3d pointOnOtherCurve)
        {
            return ComputeServer.Post<bool, Point3d, Point3d>(ApiAddress(), out pointOnThisCurve, out pointOnOtherCurve, curve, otherCurve);
        }

        /// <summary>
        /// Gets closest points between this and another curves.
        /// </summary>
        /// <param name="otherCurve">The other curve.</param>
        /// <param name="pointOnThisCurve">The point on this curve. This out parameter is assigned during this call.</param>
        /// <param name="pointOnOtherCurve">The point on other curve. This out parameter is assigned during this call.</param>
        /// <returns>true on success; false on error.</returns>
        public static bool ClosestPoints(Remote<Curve> curve, Remote<Curve> otherCurve, out Point3d pointOnThisCurve, out Point3d pointOnOtherCurve)
        {
            return ComputeServer.Post<bool, Point3d, Point3d>(ApiAddress(), out pointOnThisCurve, out pointOnOtherCurve, curve, otherCurve);
        }

        /// <summary>
        /// Computes the relationship between a point and a closed curve region. 
        /// This curve must be closed or the return value will be Unset.
        /// Both curve and point are projected to the World XY plane.
        /// </summary>
        /// <param name="testPoint">Point to test.</param>
        /// <returns>Relationship between point and curve region.</returns>
        /// <deprecated>6.0</deprecated>
        public static PointContainment Contains(this Curve curve, Point3d testPoint)
        {
            return ComputeServer.Post<PointContainment>(ApiAddress(), curve, testPoint);
        }

        /// <summary>
        /// Computes the relationship between a point and a closed curve region. 
        /// This curve must be closed or the return value will be Unset.
        /// Both curve and point are projected to the World XY plane.
        /// </summary>
        /// <param name="testPoint">Point to test.</param>
        /// <returns>Relationship between point and curve region.</returns>
        /// <deprecated>6.0</deprecated>
        public static PointContainment Contains(Remote<Curve> curve, Point3d testPoint)
        {
            return ComputeServer.Post<PointContainment>(ApiAddress(), curve, testPoint);
        }

        /// <summary>
        /// Computes the relationship between a point and a closed curve region. 
        /// This curve must be closed or the return value will be Unset.
        /// </summary>
        /// <param name="testPoint">Point to test.</param>
        /// <param name="plane">Plane in which to compare point and region.</param>
        /// <returns>Relationship between point and curve region.</returns>
        /// <deprecated>6.0</deprecated>
        public static PointContainment Contains(this Curve curve, Point3d testPoint, Plane plane)
        {
            return ComputeServer.Post<PointContainment>(ApiAddress(), curve, testPoint, plane);
        }

        /// <summary>
        /// Computes the relationship between a point and a closed curve region. 
        /// This curve must be closed or the return value will be Unset.
        /// </summary>
        /// <param name="testPoint">Point to test.</param>
        /// <param name="plane">Plane in which to compare point and region.</param>
        /// <returns>Relationship between point and curve region.</returns>
        /// <deprecated>6.0</deprecated>
        public static PointContainment Contains(Remote<Curve> curve, Point3d testPoint, Plane plane)
        {
            return ComputeServer.Post<PointContainment>(ApiAddress(), curve, testPoint, plane);
        }

        /// <summary>
        /// Computes the relationship between a point and a closed curve region. 
        /// This curve must be closed or the return value will be Unset.
        /// </summary>
        /// <param name="testPoint">Point to test.</param>
        /// <param name="plane">Plane in which to compare point and region.</param>
        /// <param name="tolerance">Tolerance to use during comparison.</param>
        /// <returns>Relationship between point and curve region.</returns>
        public static PointContainment Contains(this Curve curve, Point3d testPoint, Plane plane, double tolerance)
        {
            return ComputeServer.Post<PointContainment>(ApiAddress(), curve, testPoint, plane, tolerance);
        }

        /// <summary>
        /// Computes the relationship between a point and a closed curve region. 
        /// This curve must be closed or the return value will be Unset.
        /// </summary>
        /// <param name="testPoint">Point to test.</param>
        /// <param name="plane">Plane in which to compare point and region.</param>
        /// <param name="tolerance">Tolerance to use during comparison.</param>
        /// <returns>Relationship between point and curve region.</returns>
        public static PointContainment Contains(Remote<Curve> curve, Point3d testPoint, Plane plane, double tolerance)
        {
            return ComputeServer.Post<PointContainment>(ApiAddress(), curve, testPoint, plane, tolerance);
        }

        /// <summary>
        /// Returns the parameter values of all local extrema. 
        /// Parameter values are in increasing order so consecutive extrema 
        /// define an interval on which each component of the curve is monotone. 
        /// Note, non-periodic curves always return the end points.
        /// </summary>
        /// <param name="direction">The direction in which to perform the calculation.</param>
        /// <returns>The parameter values of all local extrema.</returns>
        public static double[] ExtremeParameters(this Curve curve, Vector3d direction)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, direction);
        }

        /// <summary>
        /// Returns the parameter values of all local extrema. 
        /// Parameter values are in increasing order so consecutive extrema 
        /// define an interval on which each component of the curve is monotone. 
        /// Note, non-periodic curves always return the end points.
        /// </summary>
        /// <param name="direction">The direction in which to perform the calculation.</param>
        /// <returns>The parameter values of all local extrema.</returns>
        public static double[] ExtremeParameters(Remote<Curve> curve, Vector3d direction)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, direction);
        }

        /// <summary>
        /// Removes kinks from a curve. Periodic curves deform smoothly without kinks.
        /// </summary>
        /// <param name="curve">The curve to make periodic. Curve must have degree >= 2.</param>
        /// <returns>The resulting curve if successful, null otherwise.</returns>
        public static Curve CreatePeriodicCurve(Curve curve)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve);
        }

        /// <summary>
        /// Removes kinks from a curve. Periodic curves deform smoothly without kinks.
        /// </summary>
        /// <param name="curve">The curve to make periodic. Curve must have degree >= 2.</param>
        /// <returns>The resulting curve if successful, null otherwise.</returns>
        public static Curve CreatePeriodicCurve(Remote<Curve> curve)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve);
        }

        /// <summary>
        /// Removes kinks from a curve. Periodic curves deform smoothly without kinks.
        /// </summary>
        /// <param name="curve">The curve to make periodic. Curve must have degree >= 2.</param>
        /// <param name="smooth">
        /// If true, smooths any kinks in the curve and moves control points to make a smooth curve. 
        /// If false, control point locations are not changed or changed minimally (only one point may move) and only the knot vector is altered.
        /// </param>
        /// <returns>The resulting curve if successful, null otherwise.</returns>
        public static Curve CreatePeriodicCurve(Curve curve, bool smooth)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, smooth);
        }

        /// <summary>
        /// Removes kinks from a curve. Periodic curves deform smoothly without kinks.
        /// </summary>
        /// <param name="curve">The curve to make periodic. Curve must have degree >= 2.</param>
        /// <param name="smooth">
        /// If true, smooths any kinks in the curve and moves control points to make a smooth curve. 
        /// If false, control point locations are not changed or changed minimally (only one point may move) and only the knot vector is altered.
        /// </param>
        /// <returns>The resulting curve if successful, null otherwise.</returns>
        public static Curve CreatePeriodicCurve(Remote<Curve> curve, bool smooth)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, smooth);
        }

        /// <summary>
        /// Gets a point at a certain length along the curve. The length must be 
        /// non-negative and less than or equal to the length of the curve. 
        /// Lengths will not be wrapped when the curve is closed or periodic.
        /// </summary>
        /// <param name="length">Length along the curve between the start point and the returned point.</param>
        /// <returns>Point on the curve at the specified length from the start point or Poin3d.Unset on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_arclengthpoint.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_arclengthpoint.cs' lang='cs'/>
        /// <code source='examples\py\ex_arclengthpoint.py' lang='py'/>
        /// </example>
        public static Point3d PointAtLength(this Curve curve, double length)
        {
            return ComputeServer.Post<Point3d>(ApiAddress(), curve, length);
        }

        /// <summary>
        /// Gets a point at a certain length along the curve. The length must be 
        /// non-negative and less than or equal to the length of the curve. 
        /// Lengths will not be wrapped when the curve is closed or periodic.
        /// </summary>
        /// <param name="length">Length along the curve between the start point and the returned point.</param>
        /// <returns>Point on the curve at the specified length from the start point or Poin3d.Unset on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_arclengthpoint.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_arclengthpoint.cs' lang='cs'/>
        /// <code source='examples\py\ex_arclengthpoint.py' lang='py'/>
        /// </example>
        public static Point3d PointAtLength(Remote<Curve> curve, double length)
        {
            return ComputeServer.Post<Point3d>(ApiAddress(), curve, length);
        }

        /// <summary>
        /// Gets a point at a certain normalized length along the curve. The length must be 
        /// between or including 0.0 and 1.0, where 0.0 equals the start of the curve and 
        /// 1.0 equals the end of the curve. 
        /// </summary>
        /// <param name="length">Normalized length along the curve between the start point and the returned point.</param>
        /// <returns>Point on the curve at the specified normalized length from the start point or Poin3d.Unset on failure.</returns>
        public static Point3d PointAtNormalizedLength(this Curve curve, double length)
        {
            return ComputeServer.Post<Point3d>(ApiAddress(), curve, length);
        }

        /// <summary>
        /// Gets a point at a certain normalized length along the curve. The length must be 
        /// between or including 0.0 and 1.0, where 0.0 equals the start of the curve and 
        /// 1.0 equals the end of the curve. 
        /// </summary>
        /// <param name="length">Normalized length along the curve between the start point and the returned point.</param>
        /// <returns>Point on the curve at the specified normalized length from the start point or Poin3d.Unset on failure.</returns>
        public static Point3d PointAtNormalizedLength(Remote<Curve> curve, double length)
        {
            return ComputeServer.Post<Point3d>(ApiAddress(), curve, length);
        }

        /// <summary>
        /// Return a 3d frame at a parameter. This is slightly different than FrameAt in
        /// that the frame is computed in a way so there is minimal rotation from one
        /// frame to the next.
        /// </summary>
        /// <param name="t">Evaluation parameter.</param>
        /// <param name="plane">The frame is returned here.</param>
        /// <returns>true on success, false on failure.</returns>
        public static bool PerpendicularFrameAt(this Curve curve, double t, out Plane plane)
        {
            return ComputeServer.Post<bool, Plane>(ApiAddress(), out plane, curve, t);
        }

        /// <summary>
        /// Return a 3d frame at a parameter. This is slightly different than FrameAt in
        /// that the frame is computed in a way so there is minimal rotation from one
        /// frame to the next.
        /// </summary>
        /// <param name="t">Evaluation parameter.</param>
        /// <param name="plane">The frame is returned here.</param>
        /// <returns>true on success, false on failure.</returns>
        public static bool PerpendicularFrameAt(Remote<Curve> curve, double t, out Plane plane)
        {
            return ComputeServer.Post<bool, Plane>(ApiAddress(), out plane, curve, t);
        }

        /// <summary>
        /// Gets a collection of perpendicular frames along the curve. Perpendicular frames 
        /// are also known as 'Zero-twisting frames' and they minimize rotation from one frame to the next.
        /// </summary>
        /// <param name="parameters">A collection of <i>strictly increasing</i> curve parameters to place perpendicular frames on.</param>
        /// <returns>An array of perpendicular frames on success or null on failure.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the curve parameters are not increasing.</exception>
        public static Plane[] GetPerpendicularFrames(this Curve curve, IEnumerable<double> parameters)
        {
            return ComputeServer.Post<Plane[]>(ApiAddress(), curve, parameters);
        }

        /// <summary>
        /// Gets a collection of perpendicular frames along the curve. Perpendicular frames 
        /// are also known as 'Zero-twisting frames' and they minimize rotation from one frame to the next.
        /// </summary>
        /// <param name="parameters">A collection of <i>strictly increasing</i> curve parameters to place perpendicular frames on.</param>
        /// <returns>An array of perpendicular frames on success or null on failure.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the curve parameters are not increasing.</exception>
        public static Plane[] GetPerpendicularFrames(Remote<Curve> curve, IEnumerable<double> parameters)
        {
            return ComputeServer.Post<Plane[]>(ApiAddress(), curve, parameters);
        }

        /// <summary>
        /// Gets the length of the curve with a fractional tolerance of 1.0e-8.
        /// </summary>
        /// <returns>The length of the curve on success, or zero on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_arclengthpoint.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_arclengthpoint.cs' lang='cs'/>
        /// <code source='examples\py\ex_arclengthpoint.py' lang='py'/>
        /// </example>
        public static double GetLength(this Curve curve)
        {
            return ComputeServer.Post<double>(ApiAddress(), curve);
        }

        /// <summary>
        /// Gets the length of the curve with a fractional tolerance of 1.0e-8.
        /// </summary>
        /// <returns>The length of the curve on success, or zero on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_arclengthpoint.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_arclengthpoint.cs' lang='cs'/>
        /// <code source='examples\py\ex_arclengthpoint.py' lang='py'/>
        /// </example>
        public static double GetLength(Remote<Curve> curve)
        {
            return ComputeServer.Post<double>(ApiAddress(), curve);
        }

        /// <summary>Get the length of the curve.</summary>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision. 
        /// fabs(("exact" length from start to t) - arc_length)/arc_length &lt;= fractionalTolerance.
        /// </param>
        /// <returns>The length of the curve on success, or zero on failure.</returns>
        public static double GetLength(this Curve curve, double fractionalTolerance)
        {
            return ComputeServer.Post<double>(ApiAddress(), curve, fractionalTolerance);
        }

        /// <summary>Get the length of the curve.</summary>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision. 
        /// fabs(("exact" length from start to t) - arc_length)/arc_length &lt;= fractionalTolerance.
        /// </param>
        /// <returns>The length of the curve on success, or zero on failure.</returns>
        public static double GetLength(Remote<Curve> curve, double fractionalTolerance)
        {
            return ComputeServer.Post<double>(ApiAddress(), curve, fractionalTolerance);
        }

        /// <summary>Get the length of a sub-section of the curve with a fractional tolerance of 1e-8.</summary>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve (must be non-decreasing).
        /// </param>
        /// <returns>The length of the sub-curve on success, or zero on failure.</returns>
        public static double GetLength(this Curve curve, Interval subdomain)
        {
            return ComputeServer.Post<double>(ApiAddress(), curve, subdomain);
        }

        /// <summary>Get the length of a sub-section of the curve with a fractional tolerance of 1e-8.</summary>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve (must be non-decreasing).
        /// </param>
        /// <returns>The length of the sub-curve on success, or zero on failure.</returns>
        public static double GetLength(Remote<Curve> curve, Interval subdomain)
        {
            return ComputeServer.Post<double>(ApiAddress(), curve, subdomain);
        }

        /// <summary>Get the length of a sub-section of the curve.</summary>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision. 
        /// fabs(("exact" length from start to t) - arc_length)/arc_length &lt;= fractionalTolerance.
        /// </param>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve (must be non-decreasing).
        /// </param>
        /// <returns>The length of the sub-curve on success, or zero on failure.</returns>
        public static double GetLength(this Curve curve, double fractionalTolerance, Interval subdomain)
        {
            return ComputeServer.Post<double>(ApiAddress(), curve, fractionalTolerance, subdomain);
        }

        /// <summary>Get the length of a sub-section of the curve.</summary>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision. 
        /// fabs(("exact" length from start to t) - arc_length)/arc_length &lt;= fractionalTolerance.
        /// </param>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve (must be non-decreasing).
        /// </param>
        /// <returns>The length of the sub-curve on success, or zero on failure.</returns>
        public static double GetLength(Remote<Curve> curve, double fractionalTolerance, Interval subdomain)
        {
            return ComputeServer.Post<double>(ApiAddress(), curve, fractionalTolerance, subdomain);
        }

        /// <summary>Used to quickly find short curves.</summary>
        /// <param name="tolerance">Length threshold value for "shortness".</param>
        /// <returns>true if the length of the curve is &lt;= tolerance.</returns>
        /// <remarks>Faster than calling Length() and testing the result.</remarks>
        /// <example>
        /// <code source='examples\vbnet\ex_dividebylength.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_dividebylength.cs' lang='cs'/>
        /// <code source='examples\py\ex_dividebylength.py' lang='py'/>
        /// </example>
        public static bool IsShort(this Curve curve, double tolerance)
        {
            return ComputeServer.Post<bool>(ApiAddress(), curve, tolerance);
        }

        /// <summary>Used to quickly find short curves.</summary>
        /// <param name="tolerance">Length threshold value for "shortness".</param>
        /// <returns>true if the length of the curve is &lt;= tolerance.</returns>
        /// <remarks>Faster than calling Length() and testing the result.</remarks>
        /// <example>
        /// <code source='examples\vbnet\ex_dividebylength.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_dividebylength.cs' lang='cs'/>
        /// <code source='examples\py\ex_dividebylength.py' lang='py'/>
        /// </example>
        public static bool IsShort(Remote<Curve> curve, double tolerance)
        {
            return ComputeServer.Post<bool>(ApiAddress(), curve, tolerance);
        }

        /// <summary>Used to quickly find short curves.</summary>
        /// <param name="tolerance">Length threshold value for "shortness".</param>
        /// <param name="subdomain">
        /// The test is performed on the interval that is the intersection of sub-domain with Domain()
        /// </param>
        /// <returns>true if the length of the curve is &lt;= tolerance.</returns>
        /// <remarks>Faster than calling Length() and testing the result.</remarks>
        public static bool IsShort(this Curve curve, double tolerance, Interval subdomain)
        {
            return ComputeServer.Post<bool>(ApiAddress(), curve, tolerance, subdomain);
        }

        /// <summary>Used to quickly find short curves.</summary>
        /// <param name="tolerance">Length threshold value for "shortness".</param>
        /// <param name="subdomain">
        /// The test is performed on the interval that is the intersection of sub-domain with Domain()
        /// </param>
        /// <returns>true if the length of the curve is &lt;= tolerance.</returns>
        /// <remarks>Faster than calling Length() and testing the result.</remarks>
        public static bool IsShort(Remote<Curve> curve, double tolerance, Interval subdomain)
        {
            return ComputeServer.Post<bool>(ApiAddress(), curve, tolerance, subdomain);
        }

        /// <summary>
        /// Looks for segments that are shorter than tolerance that can be removed. 
        /// Does not change the domain, but it will change the relative parameterization.
        /// </summary>
        /// <param name="tolerance">Tolerance which defines "short" segments.</param>
        /// <returns>
        /// true if removable short segments were found. 
        /// false if no removable short segments were found.
        /// </returns>
        public static bool RemoveShortSegments(this Curve curve, out Curve updatedInstance, double tolerance)
        {
            return ComputeServer.Post<bool, Curve>(ApiAddress(), out updatedInstance, curve, tolerance);
        }

        /// <summary>
        /// Looks for segments that are shorter than tolerance that can be removed. 
        /// Does not change the domain, but it will change the relative parameterization.
        /// </summary>
        /// <param name="tolerance">Tolerance which defines "short" segments.</param>
        /// <returns>
        /// true if removable short segments were found. 
        /// false if no removable short segments were found.
        /// </returns>
        public static bool RemoveShortSegments(Remote<Curve> curve, out Curve updatedInstance, double tolerance)
        {
            return ComputeServer.Post<bool, Curve>(ApiAddress(), out updatedInstance, curve, tolerance);
        }

        /// <summary>
        /// Gets the parameter along the curve which coincides with a given length along the curve. 
        /// A fractional tolerance of 1e-8 is used in this version of the function.
        /// </summary>
        /// <param name="segmentLength">
        /// Length of segment to measure. Must be less than or equal to the length of the curve.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from the curve start point to t equals length.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool LengthParameter(this Curve curve, double segmentLength, out double t)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, segmentLength);
        }

        /// <summary>
        /// Gets the parameter along the curve which coincides with a given length along the curve. 
        /// A fractional tolerance of 1e-8 is used in this version of the function.
        /// </summary>
        /// <param name="segmentLength">
        /// Length of segment to measure. Must be less than or equal to the length of the curve.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from the curve start point to t equals length.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool LengthParameter(Remote<Curve> curve, double segmentLength, out double t)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, segmentLength);
        }

        /// <summary>
        /// Gets the parameter along the curve which coincides with a given length along the curve.
        /// </summary>
        /// <param name="segmentLength">
        /// Length of segment to measure. Must be less than or equal to the length of the curve.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from the curve start point to t equals s.
        /// </param>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision.
        /// fabs(("exact" length from start to t) - arc_length)/arc_length &lt;= fractionalTolerance.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool LengthParameter(this Curve curve, double segmentLength, out double t, double fractionalTolerance)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, segmentLength, fractionalTolerance);
        }

        /// <summary>
        /// Gets the parameter along the curve which coincides with a given length along the curve.
        /// </summary>
        /// <param name="segmentLength">
        /// Length of segment to measure. Must be less than or equal to the length of the curve.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from the curve start point to t equals s.
        /// </param>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision.
        /// fabs(("exact" length from start to t) - arc_length)/arc_length &lt;= fractionalTolerance.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool LengthParameter(Remote<Curve> curve, double segmentLength, out double t, double fractionalTolerance)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, segmentLength, fractionalTolerance);
        }

        /// <summary>
        /// Gets the parameter along the curve which coincides with a given length along the curve. 
        /// A fractional tolerance of 1e-8 is used in this version of the function.
        /// </summary>
        /// <param name="segmentLength">
        /// Length of segment to measure. Must be less than or equal to the length of the sub-domain.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from the start of the sub-domain to t is s.
        /// </param>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve rather than the whole curve.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool LengthParameter(this Curve curve, double segmentLength, out double t, Interval subdomain)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, segmentLength, subdomain);
        }

        /// <summary>
        /// Gets the parameter along the curve which coincides with a given length along the curve. 
        /// A fractional tolerance of 1e-8 is used in this version of the function.
        /// </summary>
        /// <param name="segmentLength">
        /// Length of segment to measure. Must be less than or equal to the length of the sub-domain.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from the start of the sub-domain to t is s.
        /// </param>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve rather than the whole curve.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool LengthParameter(Remote<Curve> curve, double segmentLength, out double t, Interval subdomain)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, segmentLength, subdomain);
        }

        /// <summary>
        /// Gets the parameter along the curve which coincides with a given length along the curve.
        /// </summary>
        /// <param name="segmentLength">
        /// Length of segment to measure. Must be less than or equal to the length of the sub-domain.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from the start of the sub-domain to t is s.
        /// </param>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision. 
        /// fabs(("exact" length from start to t) - arc_length)/arc_length &lt;= fractionalTolerance.
        /// </param>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve rather than the whole curve.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool LengthParameter(this Curve curve, double segmentLength, out double t, double fractionalTolerance, Interval subdomain)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, segmentLength, fractionalTolerance, subdomain);
        }

        /// <summary>
        /// Gets the parameter along the curve which coincides with a given length along the curve.
        /// </summary>
        /// <param name="segmentLength">
        /// Length of segment to measure. Must be less than or equal to the length of the sub-domain.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from the start of the sub-domain to t is s.
        /// </param>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision. 
        /// fabs(("exact" length from start to t) - arc_length)/arc_length &lt;= fractionalTolerance.
        /// </param>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve rather than the whole curve.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool LengthParameter(Remote<Curve> curve, double segmentLength, out double t, double fractionalTolerance, Interval subdomain)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, segmentLength, fractionalTolerance, subdomain);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve. 
        /// A fractional tolerance of 1e-8 is used in this version of the function.
        /// </summary>
        /// <param name="s">
        /// Normalized arc length parameter. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from its start to t is arc_length.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool NormalizedLengthParameter(this Curve curve, double s, out double t)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, s);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve. 
        /// A fractional tolerance of 1e-8 is used in this version of the function.
        /// </summary>
        /// <param name="s">
        /// Normalized arc length parameter. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from its start to t is arc_length.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool NormalizedLengthParameter(Remote<Curve> curve, double s, out double t)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, s);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve.
        /// </summary>
        /// <param name="s">
        /// Normalized arc length parameter. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from its start to t is arc_length.
        /// </param>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision. 
        /// fabs(("exact" length from start to t) - arc_length)/arc_length &lt;= fractionalTolerance.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool NormalizedLengthParameter(this Curve curve, double s, out double t, double fractionalTolerance)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, s, fractionalTolerance);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve.
        /// </summary>
        /// <param name="s">
        /// Normalized arc length parameter. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from its start to t is arc_length.
        /// </param>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision. 
        /// fabs(("exact" length from start to t) - arc_length)/arc_length &lt;= fractionalTolerance.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool NormalizedLengthParameter(Remote<Curve> curve, double s, out double t, double fractionalTolerance)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, s, fractionalTolerance);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve. 
        /// A fractional tolerance of 1e-8 is used in this version of the function.
        /// </summary>
        /// <param name="s">
        /// Normalized arc length parameter. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from its start to t is arc_length.
        /// </param>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool NormalizedLengthParameter(this Curve curve, double s, out double t, Interval subdomain)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, s, subdomain);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve. 
        /// A fractional tolerance of 1e-8 is used in this version of the function.
        /// </summary>
        /// <param name="s">
        /// Normalized arc length parameter. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from its start to t is arc_length.
        /// </param>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool NormalizedLengthParameter(Remote<Curve> curve, double s, out double t, Interval subdomain)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, s, subdomain);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve.
        /// </summary>
        /// <param name="s">
        /// Normalized arc length parameter. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from its start to t is arc_length.
        /// </param>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision. 
        /// fabs(("exact" length from start to t) - arc_length)/arc_length &lt;= fractionalTolerance.
        /// </param>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool NormalizedLengthParameter(this Curve curve, double s, out double t, double fractionalTolerance, Interval subdomain)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, s, fractionalTolerance, subdomain);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve.
        /// </summary>
        /// <param name="s">
        /// Normalized arc length parameter. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="t">
        /// Parameter such that the length of the curve from its start to t is arc_length.
        /// </param>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision. 
        /// fabs(("exact" length from start to t) - arc_length)/arc_length &lt;= fractionalTolerance.
        /// </param>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve.
        /// </param>
        /// <returns>true on success, false on failure.</returns>
        public static bool NormalizedLengthParameter(Remote<Curve> curve, double s, out double t, double fractionalTolerance, Interval subdomain)
        {
            return ComputeServer.Post<bool, double>(ApiAddress(), out t, curve, s, fractionalTolerance, subdomain);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve. 
        /// A fractional tolerance of 1e-8 is used in this version of the function.
        /// </summary>
        /// <param name="s">
        /// Array of normalized arc length parameters. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="absoluteTolerance">
        /// If absoluteTolerance > 0, then the difference between (s[i+1]-s[i])*curve_length 
        /// and the length of the curve segment from t[i] to t[i+1] will be &lt;= absoluteTolerance.
        /// </param>
        /// <returns>
        /// If successful, array of curve parameters such that the length of the curve from its start to t[i] is s[i]*curve_length. 
        /// Null on failure.
        /// </returns>
        public static double[] NormalizedLengthParameters(this Curve curve, double[] s, double absoluteTolerance)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, s, absoluteTolerance);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve. 
        /// A fractional tolerance of 1e-8 is used in this version of the function.
        /// </summary>
        /// <param name="s">
        /// Array of normalized arc length parameters. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="absoluteTolerance">
        /// If absoluteTolerance > 0, then the difference between (s[i+1]-s[i])*curve_length 
        /// and the length of the curve segment from t[i] to t[i+1] will be &lt;= absoluteTolerance.
        /// </param>
        /// <returns>
        /// If successful, array of curve parameters such that the length of the curve from its start to t[i] is s[i]*curve_length. 
        /// Null on failure.
        /// </returns>
        public static double[] NormalizedLengthParameters(Remote<Curve> curve, double[] s, double absoluteTolerance)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, s, absoluteTolerance);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve.
        /// </summary>
        /// <param name="s">
        /// Array of normalized arc length parameters. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="absoluteTolerance">
        /// If absoluteTolerance > 0, then the difference between (s[i+1]-s[i])*curve_length 
        /// and the length of the curve segment from t[i] to t[i+1] will be &lt;= absoluteTolerance.
        /// </param>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision for each segment. 
        /// fabs("true" length - actual length)/(actual length) &lt;= fractionalTolerance.
        /// </param>
        /// <returns>
        /// If successful, array of curve parameters such that the length of the curve from its start to t[i] is s[i]*curve_length. 
        /// Null on failure.
        /// </returns>
        public static double[] NormalizedLengthParameters(this Curve curve, double[] s, double absoluteTolerance, double fractionalTolerance)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, s, absoluteTolerance, fractionalTolerance);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve.
        /// </summary>
        /// <param name="s">
        /// Array of normalized arc length parameters. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="absoluteTolerance">
        /// If absoluteTolerance > 0, then the difference between (s[i+1]-s[i])*curve_length 
        /// and the length of the curve segment from t[i] to t[i+1] will be &lt;= absoluteTolerance.
        /// </param>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision for each segment. 
        /// fabs("true" length - actual length)/(actual length) &lt;= fractionalTolerance.
        /// </param>
        /// <returns>
        /// If successful, array of curve parameters such that the length of the curve from its start to t[i] is s[i]*curve_length. 
        /// Null on failure.
        /// </returns>
        public static double[] NormalizedLengthParameters(Remote<Curve> curve, double[] s, double absoluteTolerance, double fractionalTolerance)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, s, absoluteTolerance, fractionalTolerance);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve. 
        /// A fractional tolerance of 1e-8 is used in this version of the function.
        /// </summary>
        /// <param name="s">
        /// Array of normalized arc length parameters. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="absoluteTolerance">
        /// If absoluteTolerance > 0, then the difference between (s[i+1]-s[i])*curve_length 
        /// and the length of the curve segment from t[i] to t[i+1] will be &lt;= absoluteTolerance.
        /// </param>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve. 
        /// A 0.0 s value corresponds to sub-domain->Min() and a 1.0 s value corresponds to sub-domain->Max().
        /// </param>
        /// <returns>
        /// If successful, array of curve parameters such that the length of the curve from its start to t[i] is s[i]*curve_length. 
        /// Null on failure.
        /// </returns>
        public static double[] NormalizedLengthParameters(this Curve curve, double[] s, double absoluteTolerance, Interval subdomain)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, s, absoluteTolerance, subdomain);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve. 
        /// A fractional tolerance of 1e-8 is used in this version of the function.
        /// </summary>
        /// <param name="s">
        /// Array of normalized arc length parameters. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="absoluteTolerance">
        /// If absoluteTolerance > 0, then the difference between (s[i+1]-s[i])*curve_length 
        /// and the length of the curve segment from t[i] to t[i+1] will be &lt;= absoluteTolerance.
        /// </param>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve. 
        /// A 0.0 s value corresponds to sub-domain->Min() and a 1.0 s value corresponds to sub-domain->Max().
        /// </param>
        /// <returns>
        /// If successful, array of curve parameters such that the length of the curve from its start to t[i] is s[i]*curve_length. 
        /// Null on failure.
        /// </returns>
        public static double[] NormalizedLengthParameters(Remote<Curve> curve, double[] s, double absoluteTolerance, Interval subdomain)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, s, absoluteTolerance, subdomain);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve.
        /// </summary>
        /// <param name="s">
        /// Array of normalized arc length parameters. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="absoluteTolerance">
        /// If absoluteTolerance > 0, then the difference between (s[i+1]-s[i])*curve_length 
        /// and the length of the curve segment from t[i] to t[i+1] will be &lt;= absoluteTolerance.
        /// </param>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision for each segment. 
        /// fabs("true" length - actual length)/(actual length) &lt;= fractionalTolerance.
        /// </param>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve. 
        /// A 0.0 s value corresponds to sub-domain->Min() and a 1.0 s value corresponds to sub-domain->Max().
        /// </param>
        /// <returns>
        /// If successful, array of curve parameters such that the length of the curve from its start to t[i] is s[i]*curve_length. 
        /// Null on failure.
        /// </returns>
        public static double[] NormalizedLengthParameters(this Curve curve, double[] s, double absoluteTolerance, double fractionalTolerance, Interval subdomain)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, s, absoluteTolerance, fractionalTolerance, subdomain);
        }

        /// <summary>
        /// Input the parameter of the point on the curve that is a prescribed arc length from the start of the curve.
        /// </summary>
        /// <param name="s">
        /// Array of normalized arc length parameters. 
        /// E.g., 0 = start of curve, 1/2 = midpoint of curve, 1 = end of curve.
        /// </param>
        /// <param name="absoluteTolerance">
        /// If absoluteTolerance > 0, then the difference between (s[i+1]-s[i])*curve_length 
        /// and the length of the curve segment from t[i] to t[i+1] will be &lt;= absoluteTolerance.
        /// </param>
        /// <param name="fractionalTolerance">
        /// Desired fractional precision for each segment. 
        /// fabs("true" length - actual length)/(actual length) &lt;= fractionalTolerance.
        /// </param>
        /// <param name="subdomain">
        /// The calculation is performed on the specified sub-domain of the curve. 
        /// A 0.0 s value corresponds to sub-domain->Min() and a 1.0 s value corresponds to sub-domain->Max().
        /// </param>
        /// <returns>
        /// If successful, array of curve parameters such that the length of the curve from its start to t[i] is s[i]*curve_length. 
        /// Null on failure.
        /// </returns>
        public static double[] NormalizedLengthParameters(Remote<Curve> curve, double[] s, double absoluteTolerance, double fractionalTolerance, Interval subdomain)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, s, absoluteTolerance, fractionalTolerance, subdomain);
        }

        /// <summary>
        /// Divide the curve into a number of equal-length segments.
        /// </summary>
        /// <param name="segmentCount">Segment count. Note that the number of division points may differ from the segment count.</param>
        /// <param name="includeEnds">If true, then the point at the start of the first division segment is returned.</param>
        /// <returns>
        /// List of curve parameters at the division points on success, null on failure.
        /// </returns>
        public static double[] DivideByCount(this Curve curve, int segmentCount, bool includeEnds)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, segmentCount, includeEnds);
        }

        /// <summary>
        /// Divide the curve into a number of equal-length segments.
        /// </summary>
        /// <param name="segmentCount">Segment count. Note that the number of division points may differ from the segment count.</param>
        /// <param name="includeEnds">If true, then the point at the start of the first division segment is returned.</param>
        /// <returns>
        /// List of curve parameters at the division points on success, null on failure.
        /// </returns>
        public static double[] DivideByCount(Remote<Curve> curve, int segmentCount, bool includeEnds)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, segmentCount, includeEnds);
        }

        /// <summary>
        /// Divide the curve into a number of equal-length segments.
        /// </summary>
        /// <param name="segmentCount">Segment count. Note that the number of division points may differ from the segment count.</param>
        /// <param name="includeEnds">If true, then the point at the start of the first division segment is returned.</param>
        /// <param name="points">A list of division points. If the function returns successfully, this point-array will be filled in.</param>
        /// <returns>Array containing division curve parameters on success, null on failure.</returns>
        public static double[] DivideByCount(this Curve curve, int segmentCount, bool includeEnds, out Point3d[] points)
        {
            return ComputeServer.Post<double[], Point3d[]>(ApiAddress(), out points, curve, segmentCount, includeEnds);
        }

        /// <summary>
        /// Divide the curve into a number of equal-length segments.
        /// </summary>
        /// <param name="segmentCount">Segment count. Note that the number of division points may differ from the segment count.</param>
        /// <param name="includeEnds">If true, then the point at the start of the first division segment is returned.</param>
        /// <param name="points">A list of division points. If the function returns successfully, this point-array will be filled in.</param>
        /// <returns>Array containing division curve parameters on success, null on failure.</returns>
        public static double[] DivideByCount(Remote<Curve> curve, int segmentCount, bool includeEnds, out Point3d[] points)
        {
            return ComputeServer.Post<double[], Point3d[]>(ApiAddress(), out points, curve, segmentCount, includeEnds);
        }

        /// <summary>
        /// Divide the curve into specific length segments.
        /// </summary>
        /// <param name="segmentLength">The length of each and every segment (except potentially the last one).</param>
        /// <param name="includeEnds">If true, then the point at the start of the first division segment is returned.</param>
        /// <returns>Array containing division curve parameters if successful, null on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_dividebylength.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_dividebylength.cs' lang='cs'/>
        /// <code source='examples\py\ex_dividebylength.py' lang='py'/>
        /// </example>
        public static double[] DivideByLength(this Curve curve, double segmentLength, bool includeEnds)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, segmentLength, includeEnds);
        }

        /// <summary>
        /// Divide the curve into specific length segments.
        /// </summary>
        /// <param name="segmentLength">The length of each and every segment (except potentially the last one).</param>
        /// <param name="includeEnds">If true, then the point at the start of the first division segment is returned.</param>
        /// <returns>Array containing division curve parameters if successful, null on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_dividebylength.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_dividebylength.cs' lang='cs'/>
        /// <code source='examples\py\ex_dividebylength.py' lang='py'/>
        /// </example>
        public static double[] DivideByLength(Remote<Curve> curve, double segmentLength, bool includeEnds)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, segmentLength, includeEnds);
        }

        /// <summary>
        /// Divide the curve into specific length segments.
        /// </summary>
        /// <param name="segmentLength">The length of each and every segment (except potentially the last one).</param>
        /// <param name="includeEnds">If true, then the point at the start of the first division segment is returned.</param>
        /// <param name="reverse">If true, then the divisions start from the end of the curve.</param>
        /// <returns>Array containing division curve parameters if successful, null on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_dividebylength.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_dividebylength.cs' lang='cs'/>
        /// <code source='examples\py\ex_dividebylength.py' lang='py'/>
        /// </example>
        public static double[] DivideByLength(this Curve curve, double segmentLength, bool includeEnds, bool reverse)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, segmentLength, includeEnds, reverse);
        }

        /// <summary>
        /// Divide the curve into specific length segments.
        /// </summary>
        /// <param name="segmentLength">The length of each and every segment (except potentially the last one).</param>
        /// <param name="includeEnds">If true, then the point at the start of the first division segment is returned.</param>
        /// <param name="reverse">If true, then the divisions start from the end of the curve.</param>
        /// <returns>Array containing division curve parameters if successful, null on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_dividebylength.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_dividebylength.cs' lang='cs'/>
        /// <code source='examples\py\ex_dividebylength.py' lang='py'/>
        /// </example>
        public static double[] DivideByLength(Remote<Curve> curve, double segmentLength, bool includeEnds, bool reverse)
        {
            return ComputeServer.Post<double[]>(ApiAddress(), curve, segmentLength, includeEnds, reverse);
        }

        /// <summary>
        /// Divide the curve into specific length segments.
        /// </summary>
        /// <param name="segmentLength">The length of each and every segment (except potentially the last one).</param>
        /// <param name="includeEnds">If true, then the point at the start of the first division segment is returned.</param>
        /// <param name="points">If function is successful, points at each parameter value are returned in points.</param>
        /// <returns>Array containing division curve parameters if successful, null on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_dividebylength.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_dividebylength.cs' lang='cs'/>
        /// <code source='examples\py\ex_dividebylength.py' lang='py'/>
        /// </example>
        public static double[] DivideByLength(this Curve curve, double segmentLength, bool includeEnds, out Point3d[] points)
        {
            return ComputeServer.Post<double[], Point3d[]>(ApiAddress(), out points, curve, segmentLength, includeEnds);
        }

        /// <summary>
        /// Divide the curve into specific length segments.
        /// </summary>
        /// <param name="segmentLength">The length of each and every segment (except potentially the last one).</param>
        /// <param name="includeEnds">If true, then the point at the start of the first division segment is returned.</param>
        /// <param name="points">If function is successful, points at each parameter value are returned in points.</param>
        /// <returns>Array containing division curve parameters if successful, null on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_dividebylength.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_dividebylength.cs' lang='cs'/>
        /// <code source='examples\py\ex_dividebylength.py' lang='py'/>
        /// </example>
        public static double[] DivideByLength(Remote<Curve> curve, double segmentLength, bool includeEnds, out Point3d[] points)
        {
            return ComputeServer.Post<double[], Point3d[]>(ApiAddress(), out points, curve, segmentLength, includeEnds);
        }

        /// <summary>
        /// Divide the curve into specific length segments.
        /// </summary>
        /// <param name="segmentLength">The length of each and every segment (except potentially the last one).</param>
        /// <param name="includeEnds">If true, then the point at the start of the first division segment is returned.</param>
        /// <param name="reverse">If true, then the divisions start from the end of the curve.</param>
        /// <param name="points">If function is successful, points at each parameter value are returned in points.</param>
        /// <returns>Array containing division curve parameters if successful, null on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_dividebylength.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_dividebylength.cs' lang='cs'/>
        /// <code source='examples\py\ex_dividebylength.py' lang='py'/>
        /// </example>
        public static double[] DivideByLength(this Curve curve, double segmentLength, bool includeEnds, bool reverse, out Point3d[] points)
        {
            return ComputeServer.Post<double[], Point3d[]>(ApiAddress(), out points, curve, segmentLength, includeEnds, reverse);
        }

        /// <summary>
        /// Divide the curve into specific length segments.
        /// </summary>
        /// <param name="segmentLength">The length of each and every segment (except potentially the last one).</param>
        /// <param name="includeEnds">If true, then the point at the start of the first division segment is returned.</param>
        /// <param name="reverse">If true, then the divisions start from the end of the curve.</param>
        /// <param name="points">If function is successful, points at each parameter value are returned in points.</param>
        /// <returns>Array containing division curve parameters if successful, null on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_dividebylength.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_dividebylength.cs' lang='cs'/>
        /// <code source='examples\py\ex_dividebylength.py' lang='py'/>
        /// </example>
        public static double[] DivideByLength(Remote<Curve> curve, double segmentLength, bool includeEnds, bool reverse, out Point3d[] points)
        {
            return ComputeServer.Post<double[], Point3d[]>(ApiAddress(), out points, curve, segmentLength, includeEnds, reverse);
        }

        /// <summary>
        /// Calculates 3d points on a curve where the linear distance between the points is equal.
        /// </summary>
        /// <param name="distance">The distance between division points.</param>
        /// <returns>An array of equidistant points, or null on error.</returns>
        public static Point3d[] DivideEquidistant(this Curve curve, double distance)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), curve, distance);
        }

        /// <summary>
        /// Calculates 3d points on a curve where the linear distance between the points is equal.
        /// </summary>
        /// <param name="distance">The distance between division points.</param>
        /// <returns>An array of equidistant points, or null on error.</returns>
        public static Point3d[] DivideEquidistant(Remote<Curve> curve, double distance)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), curve, distance);
        }

        /// <summary>
        /// Divides this curve at fixed steps along a defined contour line.
        /// </summary>
        /// <param name="contourStart">The start of the contouring line.</param>
        /// <param name="contourEnd">The end of the contouring line.</param>
        /// <param name="interval">A distance to measure on the contouring axis.</param>
        /// <returns>An array of points; or null on error.</returns>
        public static Point3d[] DivideAsContour(this Curve curve, Point3d contourStart, Point3d contourEnd, double interval)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), curve, contourStart, contourEnd, interval);
        }

        /// <summary>
        /// Divides this curve at fixed steps along a defined contour line.
        /// </summary>
        /// <param name="contourStart">The start of the contouring line.</param>
        /// <param name="contourEnd">The end of the contouring line.</param>
        /// <param name="interval">A distance to measure on the contouring axis.</param>
        /// <returns>An array of points; or null on error.</returns>
        public static Point3d[] DivideAsContour(Remote<Curve> curve, Point3d contourStart, Point3d contourEnd, double interval)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), curve, contourStart, contourEnd, interval);
        }

        /// <summary>
        /// Shortens a curve by a given length
        /// </summary>
        /// <param name="side"></param>
        /// <param name="length"></param>
        /// <returns>Trimmed curve if successful, null on failure.</returns>
        public static Curve Trim(this Curve curve, CurveEnd side, double length)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, length);
        }

        /// <summary>
        /// Shortens a curve by a given length
        /// </summary>
        /// <param name="side"></param>
        /// <param name="length"></param>
        /// <returns>Trimmed curve if successful, null on failure.</returns>
        public static Curve Trim(Remote<Curve> curve, CurveEnd side, double length)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, length);
        }

        /// <summary>
        /// Splits a curve into pieces using a polysurface.
        /// </summary>
        /// <param name="cutter">A cutting surface or polysurface.</param>
        /// <param name="tolerance">A tolerance for computing intersections.</param>
        /// <returns>An array of curves. This array can be empty.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] Split(this Curve curve, Brep cutter, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, cutter, tolerance);
        }

        /// <summary>
        /// Splits a curve into pieces using a polysurface.
        /// </summary>
        /// <param name="cutter">A cutting surface or polysurface.</param>
        /// <param name="tolerance">A tolerance for computing intersections.</param>
        /// <returns>An array of curves. This array can be empty.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] Split(Remote<Curve> curve, Remote<Brep> cutter, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, cutter, tolerance);
        }

        /// <summary>
        /// Splits a curve into pieces using a polysurface.
        /// </summary>
        /// <param name="cutter">A cutting surface or polysurface.</param>
        /// <param name="tolerance">A tolerance for computing intersections.</param>
        /// <param name="angleToleranceRadians"></param>
        /// <returns>An array of curves. This array can be empty.</returns>
        public static Curve[] Split(this Curve curve, Brep cutter, double tolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, cutter, tolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Splits a curve into pieces using a polysurface.
        /// </summary>
        /// <param name="cutter">A cutting surface or polysurface.</param>
        /// <param name="tolerance">A tolerance for computing intersections.</param>
        /// <param name="angleToleranceRadians"></param>
        /// <returns>An array of curves. This array can be empty.</returns>
        public static Curve[] Split(Remote<Curve> curve, Remote<Brep> cutter, double tolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, cutter, tolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Splits a curve into pieces using a surface.
        /// </summary>
        /// <param name="cutter">A cutting surface or polysurface.</param>
        /// <param name="tolerance">A tolerance for computing intersections.</param>
        /// <returns>An array of curves. This array can be empty.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] Split(this Curve curve, Surface cutter, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, cutter, tolerance);
        }

        /// <summary>
        /// Splits a curve into pieces using a surface.
        /// </summary>
        /// <param name="cutter">A cutting surface or polysurface.</param>
        /// <param name="tolerance">A tolerance for computing intersections.</param>
        /// <returns>An array of curves. This array can be empty.</returns>
        /// <deprecated>6.0</deprecated>
        public static Curve[] Split(Remote<Curve> curve, Remote<Surface> cutter, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, cutter, tolerance);
        }

        /// <summary>
        /// Splits a curve into pieces using a surface.
        /// </summary>
        /// <param name="cutter">A cutting surface or polysurface.</param>
        /// <param name="tolerance">A tolerance for computing intersections.</param>
        /// <param name="angleToleranceRadians"></param>
        /// <returns>An array of curves. This array can be empty.</returns>
        public static Curve[] Split(this Curve curve, Surface cutter, double tolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, cutter, tolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Splits a curve into pieces using a surface.
        /// </summary>
        /// <param name="cutter">A cutting surface or polysurface.</param>
        /// <param name="tolerance">A tolerance for computing intersections.</param>
        /// <param name="angleToleranceRadians"></param>
        /// <returns>An array of curves. This array can be empty.</returns>
        public static Curve[] Split(Remote<Curve> curve, Remote<Surface> cutter, double tolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, cutter, tolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Where possible, analytically extends curve to include the given domain. 
        /// This will not work on closed curves. The original curve will be identical to the 
        /// restriction of the resulting curve to the original curve domain.
        /// </summary>
        /// <param name="t0">Start of extension domain, if the start is not inside the 
        /// Domain of this curve, an attempt will be made to extend the curve.</param>
        /// <param name="t1">End of extension domain, if the end is not inside the 
        /// Domain of this curve, an attempt will be made to extend the curve.</param>
        /// <returns>Extended curve on success, null on failure.</returns>
        public static Curve Extend(this Curve curve, double t0, double t1)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, t0, t1);
        }

        /// <summary>
        /// Where possible, analytically extends curve to include the given domain. 
        /// This will not work on closed curves. The original curve will be identical to the 
        /// restriction of the resulting curve to the original curve domain.
        /// </summary>
        /// <param name="t0">Start of extension domain, if the start is not inside the 
        /// Domain of this curve, an attempt will be made to extend the curve.</param>
        /// <param name="t1">End of extension domain, if the end is not inside the 
        /// Domain of this curve, an attempt will be made to extend the curve.</param>
        /// <returns>Extended curve on success, null on failure.</returns>
        public static Curve Extend(Remote<Curve> curve, double t0, double t1)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, t0, t1);
        }

        /// <summary>
        /// Where possible, analytically extends curve to include the given domain. 
        /// This will not work on closed curves. The original curve will be identical to the 
        /// restriction of the resulting curve to the original curve domain.
        /// </summary>
        /// <param name="domain">Extension domain.</param>
        /// <returns>Extended curve on success, null on failure.</returns>
        public static Curve Extend(this Curve curve, Interval domain)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, domain);
        }

        /// <summary>
        /// Where possible, analytically extends curve to include the given domain. 
        /// This will not work on closed curves. The original curve will be identical to the 
        /// restriction of the resulting curve to the original curve domain.
        /// </summary>
        /// <param name="domain">Extension domain.</param>
        /// <returns>Extended curve on success, null on failure.</returns>
        public static Curve Extend(Remote<Curve> curve, Interval domain)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, domain);
        }

        /// <summary>
        /// Extends a curve by a specific length.
        /// </summary>
        /// <param name="side">Curve end to extend.</param>
        /// <param name="length">Length to add to the curve end.</param>
        /// <param name="style">Extension style.</param>
        /// <returns>A curve with extended ends or null on failure.</returns>
        public static Curve Extend(this Curve curve, CurveEnd side, double length, CurveExtensionStyle style)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, length, style);
        }

        /// <summary>
        /// Extends a curve by a specific length.
        /// </summary>
        /// <param name="side">Curve end to extend.</param>
        /// <param name="length">Length to add to the curve end.</param>
        /// <param name="style">Extension style.</param>
        /// <returns>A curve with extended ends or null on failure.</returns>
        public static Curve Extend(Remote<Curve> curve, CurveEnd side, double length, CurveExtensionStyle style)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, length, style);
        }

        /// <summary>
        /// Extends a curve until it intersects a collection of objects.
        /// </summary>
        /// <param name="side">The end of the curve to extend.</param>
        /// <param name="style">The style or type of extension to use.</param>
        /// <param name="geometry">A collection of objects. Allowable object types are Curve, Surface, Brep.</param>
        /// <returns>New extended curve result on success, null on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_extendcurve.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_extendcurve.cs' lang='cs'/>
        /// <code source='examples\py\ex_extendcurve.py' lang='py'/>
        /// </example>
        public static Curve Extend(this Curve curve, CurveEnd side, CurveExtensionStyle style, System.Collections.Generic.IEnumerable<GeometryBase> geometry)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, style, geometry);
        }

        /// <summary>
        /// Extends a curve until it intersects a collection of objects.
        /// </summary>
        /// <param name="side">The end of the curve to extend.</param>
        /// <param name="style">The style or type of extension to use.</param>
        /// <param name="geometry">A collection of objects. Allowable object types are Curve, Surface, Brep.</param>
        /// <returns>New extended curve result on success, null on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_extendcurve.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_extendcurve.cs' lang='cs'/>
        /// <code source='examples\py\ex_extendcurve.py' lang='py'/>
        /// </example>
        public static Curve Extend(Remote<Curve> curve, CurveEnd side, CurveExtensionStyle style, System.Collections.Generic.IEnumerable<GeometryBase> geometry)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, style, geometry);
        }

        /// <summary>
        /// Extends a curve to a point.
        /// </summary>
        /// <param name="side">The end of the curve to extend.</param>
        /// <param name="style">The style or type of extension to use.</param>
        /// <param name="endPoint">A new end point.</param>
        /// <returns>New extended curve result on success, null on failure.</returns>
        public static Curve Extend(this Curve curve, CurveEnd side, CurveExtensionStyle style, Point3d endPoint)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, style, endPoint);
        }

        /// <summary>
        /// Extends a curve to a point.
        /// </summary>
        /// <param name="side">The end of the curve to extend.</param>
        /// <param name="style">The style or type of extension to use.</param>
        /// <param name="endPoint">A new end point.</param>
        /// <returns>New extended curve result on success, null on failure.</returns>
        public static Curve Extend(Remote<Curve> curve, CurveEnd side, CurveExtensionStyle style, Point3d endPoint)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, style, endPoint);
        }

        /// <summary>
        /// Extends a curve on a surface.
        /// </summary>
        /// <param name="side">The end of the curve to extend.</param>
        /// <param name="surface">Surface that contains the curve.</param>
        /// <returns>New extended curve result on success, null on failure.</returns>
        public static Curve ExtendOnSurface(this Curve curve, CurveEnd side, Surface surface)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, surface);
        }

        /// <summary>
        /// Extends a curve on a surface.
        /// </summary>
        /// <param name="side">The end of the curve to extend.</param>
        /// <param name="surface">Surface that contains the curve.</param>
        /// <returns>New extended curve result on success, null on failure.</returns>
        public static Curve ExtendOnSurface(Remote<Curve> curve, CurveEnd side, Remote<Surface> surface)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, surface);
        }

        /// <summary>
        /// Extends a curve on a surface.
        /// </summary>
        /// <param name="side">The end of the curve to extend.</param>
        /// <param name="face">BrepFace that contains the curve.</param>
        /// <returns>New extended curve result on success, null on failure.</returns>
        public static Curve ExtendOnSurface(this Curve curve, CurveEnd side, BrepFace face)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, face);
        }

        /// <summary>
        /// Extends a curve on a surface.
        /// </summary>
        /// <param name="side">The end of the curve to extend.</param>
        /// <param name="face">BrepFace that contains the curve.</param>
        /// <returns>New extended curve result on success, null on failure.</returns>
        public static Curve ExtendOnSurface(Remote<Curve> curve, CurveEnd side, BrepFace face)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, face);
        }

        /// <summary>
        /// Extends a curve by a line until it intersects a collection of objects.
        /// </summary>
        /// <param name="side">The end of the curve to extend.</param>
        /// <param name="geometry">A collection of objects. Allowable object types are Curve, Surface, Brep.</param>
        /// <returns>New extended curve result on success, null on failure.</returns>
        public static Curve ExtendByLine(this Curve curve, CurveEnd side, System.Collections.Generic.IEnumerable<GeometryBase> geometry)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, geometry);
        }

        /// <summary>
        /// Extends a curve by a line until it intersects a collection of objects.
        /// </summary>
        /// <param name="side">The end of the curve to extend.</param>
        /// <param name="geometry">A collection of objects. Allowable object types are Curve, Surface, Brep.</param>
        /// <returns>New extended curve result on success, null on failure.</returns>
        public static Curve ExtendByLine(Remote<Curve> curve, CurveEnd side, System.Collections.Generic.IEnumerable<GeometryBase> geometry)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, geometry);
        }

        /// <summary>
        /// Extends a curve by an Arc until it intersects a collection of objects.
        /// </summary>
        /// <param name="side">The end of the curve to extend.</param>
        /// <param name="geometry">A collection of objects. Allowable object types are Curve, Surface, Brep.</param>
        /// <returns>New extended curve result on success, null on failure.</returns>
        public static Curve ExtendByArc(this Curve curve, CurveEnd side, System.Collections.Generic.IEnumerable<GeometryBase> geometry)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, geometry);
        }

        /// <summary>
        /// Extends a curve by an Arc until it intersects a collection of objects.
        /// </summary>
        /// <param name="side">The end of the curve to extend.</param>
        /// <param name="geometry">A collection of objects. Allowable object types are Curve, Surface, Brep.</param>
        /// <returns>New extended curve result on success, null on failure.</returns>
        public static Curve ExtendByArc(Remote<Curve> curve, CurveEnd side, System.Collections.Generic.IEnumerable<GeometryBase> geometry)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, side, geometry);
        }

        /// <summary>
        /// Returns a geometrically equivalent PolyCurve.
        /// <para>The PolyCurve has the following properties</para>
        /// <para>
        ///	1. All the PolyCurve segments are LineCurve, PolylineCurve, ArcCurve, or NurbsCurve.
        /// </para>
        /// <para>
        ///	2. The NURBS Curves segments do not have fully multiple interior knots.
        /// </para>
        /// <para>
        ///	3. Rational NURBS curves do not have constant weights.
        /// </para>
        /// <para>
        ///	4. Any segment for which IsLinear() or IsArc() is true is a Line, 
        ///        Polyline segment, or an Arc.
        /// </para>
        /// <para>
        ///	5. Adjacent co-linear or co-circular segments are combined.
        /// </para>
        /// <para>
        ///	6. Segments that meet with G1-continuity have there ends tuned up so
        ///        that they meet with G1-continuity to within machine precision.
        /// </para>
        /// </summary>
        /// <param name="options">Simplification options.</param>
        /// <param name="distanceTolerance">A distance tolerance for the simplification.</param>
        /// <param name="angleToleranceRadians">An angle tolerance for the simplification.</param>
        /// <returns>New simplified curve on success, null on failure.</returns>
        public static Curve Simplify(this Curve curve, CurveSimplifyOptions options, double distanceTolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, options, distanceTolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Returns a geometrically equivalent PolyCurve.
        /// <para>The PolyCurve has the following properties</para>
        /// <para>
        ///	1. All the PolyCurve segments are LineCurve, PolylineCurve, ArcCurve, or NurbsCurve.
        /// </para>
        /// <para>
        ///	2. The NURBS Curves segments do not have fully multiple interior knots.
        /// </para>
        /// <para>
        ///	3. Rational NURBS curves do not have constant weights.
        /// </para>
        /// <para>
        ///	4. Any segment for which IsLinear() or IsArc() is true is a Line, 
        ///        Polyline segment, or an Arc.
        /// </para>
        /// <para>
        ///	5. Adjacent co-linear or co-circular segments are combined.
        /// </para>
        /// <para>
        ///	6. Segments that meet with G1-continuity have there ends tuned up so
        ///        that they meet with G1-continuity to within machine precision.
        /// </para>
        /// </summary>
        /// <param name="options">Simplification options.</param>
        /// <param name="distanceTolerance">A distance tolerance for the simplification.</param>
        /// <param name="angleToleranceRadians">An angle tolerance for the simplification.</param>
        /// <returns>New simplified curve on success, null on failure.</returns>
        public static Curve Simplify(Remote<Curve> curve, CurveSimplifyOptions options, double distanceTolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, options, distanceTolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Same as SimplifyCurve, but simplifies only the last two segments at "side" end.
        /// </summary>
        /// <param name="end">If CurveEnd.Start the function simplifies the last two start 
        /// side segments, otherwise if CurveEnd.End the last two end side segments are simplified.
        /// </param>
        /// <param name="options">Simplification options.</param>
        /// <param name="distanceTolerance">A distance tolerance for the simplification.</param>
        /// <param name="angleToleranceRadians">An angle tolerance for the simplification.</param>
        /// <returns>New simplified curve on success, null on failure.</returns>
        public static Curve SimplifyEnd(this Curve curve, CurveEnd end, CurveSimplifyOptions options, double distanceTolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, end, options, distanceTolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Same as SimplifyCurve, but simplifies only the last two segments at "side" end.
        /// </summary>
        /// <param name="end">If CurveEnd.Start the function simplifies the last two start 
        /// side segments, otherwise if CurveEnd.End the last two end side segments are simplified.
        /// </param>
        /// <param name="options">Simplification options.</param>
        /// <param name="distanceTolerance">A distance tolerance for the simplification.</param>
        /// <param name="angleToleranceRadians">An angle tolerance for the simplification.</param>
        /// <returns>New simplified curve on success, null on failure.</returns>
        public static Curve SimplifyEnd(Remote<Curve> curve, CurveEnd end, CurveSimplifyOptions options, double distanceTolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, end, options, distanceTolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Fairs a curve object. Fair works best on degree 3 (cubic) curves. Attempts to 
        /// remove large curvature variations while limiting the geometry changes to be no 
        /// more than the specified tolerance. 
        /// </summary>
        /// <param name="distanceTolerance">Maximum allowed distance the faired curve is allowed to deviate from the input.</param>
        /// <param name="angleTolerance">(in radians) kinks with angles &lt;= angleTolerance are smoothed out 0.05 is a good default.</param>
        /// <param name="clampStart">The number of (control vertices-1) to preserve at start. 
        /// <para>0 = preserve start point</para>
        /// <para>1 = preserve start point and 1st derivative</para>
        /// <para>2 = preserve start point, 1st and 2nd derivative</para>
        /// </param>
        /// <param name="clampEnd">Same as clampStart.</param>
        /// <param name="iterations">The number of iterations to use in adjusting the curve.</param>
        /// <returns>Returns new faired Curve on success, null on failure.</returns>
        public static Curve Fair(this Curve curve, double distanceTolerance, double angleTolerance, int clampStart, int clampEnd, int iterations)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, distanceTolerance, angleTolerance, clampStart, clampEnd, iterations);
        }

        /// <summary>
        /// Fairs a curve object. Fair works best on degree 3 (cubic) curves. Attempts to 
        /// remove large curvature variations while limiting the geometry changes to be no 
        /// more than the specified tolerance. 
        /// </summary>
        /// <param name="distanceTolerance">Maximum allowed distance the faired curve is allowed to deviate from the input.</param>
        /// <param name="angleTolerance">(in radians) kinks with angles &lt;= angleTolerance are smoothed out 0.05 is a good default.</param>
        /// <param name="clampStart">The number of (control vertices-1) to preserve at start. 
        /// <para>0 = preserve start point</para>
        /// <para>1 = preserve start point and 1st derivative</para>
        /// <para>2 = preserve start point, 1st and 2nd derivative</para>
        /// </param>
        /// <param name="clampEnd">Same as clampStart.</param>
        /// <param name="iterations">The number of iterations to use in adjusting the curve.</param>
        /// <returns>Returns new faired Curve on success, null on failure.</returns>
        public static Curve Fair(Remote<Curve> curve, double distanceTolerance, double angleTolerance, int clampStart, int clampEnd, int iterations)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, distanceTolerance, angleTolerance, clampStart, clampEnd, iterations);
        }

        /// <summary>
        /// Fits a new curve through an existing curve.
        /// </summary>
        /// <param name="degree">The degree of the returned Curve. Must be bigger than 1.</param>
        /// <param name="fitTolerance">The fitting tolerance. If fitTolerance is RhinoMath.UnsetValue or &lt;=0.0,
        /// the document absolute tolerance is used.</param>
        /// <param name="angleTolerance">The kink smoothing tolerance in radians.
        /// <para>If angleTolerance is 0.0, all kinks are smoothed</para>
        /// <para>If angleTolerance is &gt;0.0, kinks smaller than angleTolerance are smoothed</para>  
        /// <para>If angleTolerance is RhinoMath.UnsetValue or &lt;0.0, the document angle tolerance is used for the kink smoothing</para>
        /// </param>
        /// <returns>Returns a new fitted Curve if successful, null on failure.</returns>
        public static Curve Fit(this Curve curve, int degree, double fitTolerance, double angleTolerance)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, degree, fitTolerance, angleTolerance);
        }

        /// <summary>
        /// Fits a new curve through an existing curve.
        /// </summary>
        /// <param name="degree">The degree of the returned Curve. Must be bigger than 1.</param>
        /// <param name="fitTolerance">The fitting tolerance. If fitTolerance is RhinoMath.UnsetValue or &lt;=0.0,
        /// the document absolute tolerance is used.</param>
        /// <param name="angleTolerance">The kink smoothing tolerance in radians.
        /// <para>If angleTolerance is 0.0, all kinks are smoothed</para>
        /// <para>If angleTolerance is &gt;0.0, kinks smaller than angleTolerance are smoothed</para>  
        /// <para>If angleTolerance is RhinoMath.UnsetValue or &lt;0.0, the document angle tolerance is used for the kink smoothing</para>
        /// </param>
        /// <returns>Returns a new fitted Curve if successful, null on failure.</returns>
        public static Curve Fit(Remote<Curve> curve, int degree, double fitTolerance, double angleTolerance)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, degree, fitTolerance, angleTolerance);
        }

        /// <summary>
        /// Rebuild a curve with a specific point count.
        /// </summary>
        /// <param name="pointCount">Number of control points in the rebuild curve.</param>
        /// <param name="degree">Degree of curve. Valid values are between and including 1 and 11.</param>
        /// <param name="preserveTangents">If true, the end tangents of the input curve will be preserved.</param>
        /// <returns>A NURBS curve on success or null on failure.</returns>
        public static NurbsCurve Rebuild(this Curve curve, int pointCount, int degree, bool preserveTangents)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), curve, pointCount, degree, preserveTangents);
        }

        /// <summary>
        /// Rebuild a curve with a specific point count.
        /// </summary>
        /// <param name="pointCount">Number of control points in the rebuild curve.</param>
        /// <param name="degree">Degree of curve. Valid values are between and including 1 and 11.</param>
        /// <param name="preserveTangents">If true, the end tangents of the input curve will be preserved.</param>
        /// <returns>A NURBS curve on success or null on failure.</returns>
        public static NurbsCurve Rebuild(Remote<Curve> curve, int pointCount, int degree, bool preserveTangents)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), curve, pointCount, degree, preserveTangents);
        }

        /// <summary>
        /// Gets a polyline approximation of a curve.
        /// </summary>
        /// <param name="mainSegmentCount">
        /// If mainSegmentCount &lt;= 0, then both subSegmentCount and mainSegmentCount are ignored. 
        /// If mainSegmentCount &gt; 0, then subSegmentCount must be &gt;= 1. In this 
        /// case the NURBS will be broken into mainSegmentCount equally spaced 
        /// chords. If needed, each of these chords can be split into as many 
        /// subSegmentCount sub-parts if the subdivision is necessary for the 
        /// mesh to meet the other meshing constraints. In particular, if 
        /// subSegmentCount = 0, then the curve is broken into mainSegmentCount 
        /// pieces and no further testing is performed.</param>
        /// <param name="subSegmentCount">An amount of subsegments.</param>
        /// <param name="maxAngleRadians">
        /// ( 0 to pi ) Maximum angle (in radians) between unit tangents at 
        /// adjacent vertices.</param>
        /// <param name="maxChordLengthRatio">Maximum permitted value of 
        /// (distance chord midpoint to curve) / (length of chord).</param>
        /// <param name="maxAspectRatio">If maxAspectRatio &lt; 1.0, the parameter is ignored. 
        /// If 1 &lt;= maxAspectRatio &lt; sqrt(2), it is treated as if maxAspectRatio = sqrt(2). 
        /// This parameter controls the maximum permitted value of 
        /// (length of longest chord) / (length of shortest chord).</param>
        /// <param name="tolerance">If tolerance = 0, the parameter is ignored. 
        /// This parameter controls the maximum permitted value of the 
        /// distance from the curve to the polyline.</param>
        /// <param name="minEdgeLength">The minimum permitted edge length.</param>
        /// <param name="maxEdgeLength">If maxEdgeLength = 0, the parameter 
        /// is ignored. This parameter controls the maximum permitted edge length.
        /// </param>
        /// <param name="keepStartPoint">If true the starting point of the curve 
        /// is added to the polyline. If false the starting point of the curve is 
        /// not added to the polyline.</param>
        /// <returns>PolylineCurve on success, null on error.</returns>
        public static PolylineCurve ToPolyline(this Curve curve, int mainSegmentCount, int subSegmentCount, double maxAngleRadians, double maxChordLengthRatio, double maxAspectRatio, double tolerance, double minEdgeLength, double maxEdgeLength, bool keepStartPoint)
        {
            return ComputeServer.Post<PolylineCurve>(ApiAddress(), curve, mainSegmentCount, subSegmentCount, maxAngleRadians, maxChordLengthRatio, maxAspectRatio, tolerance, minEdgeLength, maxEdgeLength, keepStartPoint);
        }

        /// <summary>
        /// Gets a polyline approximation of a curve.
        /// </summary>
        /// <param name="mainSegmentCount">
        /// If mainSegmentCount &lt;= 0, then both subSegmentCount and mainSegmentCount are ignored. 
        /// If mainSegmentCount &gt; 0, then subSegmentCount must be &gt;= 1. In this 
        /// case the NURBS will be broken into mainSegmentCount equally spaced 
        /// chords. If needed, each of these chords can be split into as many 
        /// subSegmentCount sub-parts if the subdivision is necessary for the 
        /// mesh to meet the other meshing constraints. In particular, if 
        /// subSegmentCount = 0, then the curve is broken into mainSegmentCount 
        /// pieces and no further testing is performed.</param>
        /// <param name="subSegmentCount">An amount of subsegments.</param>
        /// <param name="maxAngleRadians">
        /// ( 0 to pi ) Maximum angle (in radians) between unit tangents at 
        /// adjacent vertices.</param>
        /// <param name="maxChordLengthRatio">Maximum permitted value of 
        /// (distance chord midpoint to curve) / (length of chord).</param>
        /// <param name="maxAspectRatio">If maxAspectRatio &lt; 1.0, the parameter is ignored. 
        /// If 1 &lt;= maxAspectRatio &lt; sqrt(2), it is treated as if maxAspectRatio = sqrt(2). 
        /// This parameter controls the maximum permitted value of 
        /// (length of longest chord) / (length of shortest chord).</param>
        /// <param name="tolerance">If tolerance = 0, the parameter is ignored. 
        /// This parameter controls the maximum permitted value of the 
        /// distance from the curve to the polyline.</param>
        /// <param name="minEdgeLength">The minimum permitted edge length.</param>
        /// <param name="maxEdgeLength">If maxEdgeLength = 0, the parameter 
        /// is ignored. This parameter controls the maximum permitted edge length.
        /// </param>
        /// <param name="keepStartPoint">If true the starting point of the curve 
        /// is added to the polyline. If false the starting point of the curve is 
        /// not added to the polyline.</param>
        /// <returns>PolylineCurve on success, null on error.</returns>
        public static PolylineCurve ToPolyline(Remote<Curve> curve, int mainSegmentCount, int subSegmentCount, double maxAngleRadians, double maxChordLengthRatio, double maxAspectRatio, double tolerance, double minEdgeLength, double maxEdgeLength, bool keepStartPoint)
        {
            return ComputeServer.Post<PolylineCurve>(ApiAddress(), curve, mainSegmentCount, subSegmentCount, maxAngleRadians, maxChordLengthRatio, maxAspectRatio, tolerance, minEdgeLength, maxEdgeLength, keepStartPoint);
        }

        /// <summary>
        /// Gets a polyline approximation of a curve.
        /// </summary>
        /// <param name="mainSegmentCount">
        /// If mainSegmentCount &lt;= 0, then both subSegmentCount and mainSegmentCount are ignored. 
        /// If mainSegmentCount &gt; 0, then subSegmentCount must be &gt;= 1. In this 
        /// case the NURBS will be broken into mainSegmentCount equally spaced 
        /// chords. If needed, each of these chords can be split into as many 
        /// subSegmentCount sub-parts if the subdivision is necessary for the 
        /// mesh to meet the other meshing constraints. In particular, if 
        /// subSegmentCount = 0, then the curve is broken into mainSegmentCount 
        /// pieces and no further testing is performed.</param>
        /// <param name="subSegmentCount">An amount of subsegments.</param>
        /// <param name="maxAngleRadians">
        /// ( 0 to pi ) Maximum angle (in radians) between unit tangents at 
        /// adjacent vertices.</param>
        /// <param name="maxChordLengthRatio">Maximum permitted value of 
        /// (distance chord midpoint to curve) / (length of chord).</param>
        /// <param name="maxAspectRatio">If maxAspectRatio &lt; 1.0, the parameter is ignored. 
        /// If 1 &lt;= maxAspectRatio &lt; sqrt(2), it is treated as if maxAspectRatio = sqrt(2). 
        /// This parameter controls the maximum permitted value of 
        /// (length of longest chord) / (length of shortest chord).</param>
        /// <param name="tolerance">If tolerance = 0, the parameter is ignored. 
        /// This parameter controls the maximum permitted value of the 
        /// distance from the curve to the polyline.</param>
        /// <param name="minEdgeLength">The minimum permitted edge length.</param>
        /// <param name="maxEdgeLength">If maxEdgeLength = 0, the parameter 
        /// is ignored. This parameter controls the maximum permitted edge length.
        /// </param>
        /// <param name="keepStartPoint">If true the starting point of the curve 
        /// is added to the polyline. If false the starting point of the curve is 
        /// not added to the polyline.</param>
        /// <param name="curveDomain">This sub-domain of the NURBS curve is approximated.</param>
        /// <returns>PolylineCurve on success, null on error.</returns>
        public static PolylineCurve ToPolyline(this Curve curve, int mainSegmentCount, int subSegmentCount, double maxAngleRadians, double maxChordLengthRatio, double maxAspectRatio, double tolerance, double minEdgeLength, double maxEdgeLength, bool keepStartPoint, Interval curveDomain)
        {
            return ComputeServer.Post<PolylineCurve>(ApiAddress(), curve, mainSegmentCount, subSegmentCount, maxAngleRadians, maxChordLengthRatio, maxAspectRatio, tolerance, minEdgeLength, maxEdgeLength, keepStartPoint, curveDomain);
        }

        /// <summary>
        /// Gets a polyline approximation of a curve.
        /// </summary>
        /// <param name="mainSegmentCount">
        /// If mainSegmentCount &lt;= 0, then both subSegmentCount and mainSegmentCount are ignored. 
        /// If mainSegmentCount &gt; 0, then subSegmentCount must be &gt;= 1. In this 
        /// case the NURBS will be broken into mainSegmentCount equally spaced 
        /// chords. If needed, each of these chords can be split into as many 
        /// subSegmentCount sub-parts if the subdivision is necessary for the 
        /// mesh to meet the other meshing constraints. In particular, if 
        /// subSegmentCount = 0, then the curve is broken into mainSegmentCount 
        /// pieces and no further testing is performed.</param>
        /// <param name="subSegmentCount">An amount of subsegments.</param>
        /// <param name="maxAngleRadians">
        /// ( 0 to pi ) Maximum angle (in radians) between unit tangents at 
        /// adjacent vertices.</param>
        /// <param name="maxChordLengthRatio">Maximum permitted value of 
        /// (distance chord midpoint to curve) / (length of chord).</param>
        /// <param name="maxAspectRatio">If maxAspectRatio &lt; 1.0, the parameter is ignored. 
        /// If 1 &lt;= maxAspectRatio &lt; sqrt(2), it is treated as if maxAspectRatio = sqrt(2). 
        /// This parameter controls the maximum permitted value of 
        /// (length of longest chord) / (length of shortest chord).</param>
        /// <param name="tolerance">If tolerance = 0, the parameter is ignored. 
        /// This parameter controls the maximum permitted value of the 
        /// distance from the curve to the polyline.</param>
        /// <param name="minEdgeLength">The minimum permitted edge length.</param>
        /// <param name="maxEdgeLength">If maxEdgeLength = 0, the parameter 
        /// is ignored. This parameter controls the maximum permitted edge length.
        /// </param>
        /// <param name="keepStartPoint">If true the starting point of the curve 
        /// is added to the polyline. If false the starting point of the curve is 
        /// not added to the polyline.</param>
        /// <param name="curveDomain">This sub-domain of the NURBS curve is approximated.</param>
        /// <returns>PolylineCurve on success, null on error.</returns>
        public static PolylineCurve ToPolyline(Remote<Curve> curve, int mainSegmentCount, int subSegmentCount, double maxAngleRadians, double maxChordLengthRatio, double maxAspectRatio, double tolerance, double minEdgeLength, double maxEdgeLength, bool keepStartPoint, Interval curveDomain)
        {
            return ComputeServer.Post<PolylineCurve>(ApiAddress(), curve, mainSegmentCount, subSegmentCount, maxAngleRadians, maxChordLengthRatio, maxAspectRatio, tolerance, minEdgeLength, maxEdgeLength, keepStartPoint, curveDomain);
        }

        /// <summary>
        /// Gets a polyline approximation of a curve.
        /// </summary>
        /// <param name="tolerance">The tolerance. This is the maximum deviation from line midpoints to the curve. When in doubt, use the document's model space absolute tolerance.</param>
        /// <param name="angleTolerance">The angle tolerance in radians. This is the maximum deviation of the line directions. When in doubt, use the document's model space angle tolerance.</param>
        /// <param name="minimumLength">The minimum segment length.</param>
        /// <param name="maximumLength">The maximum segment length.</param>
        /// <returns>PolyCurve on success, null on error.</returns>
        public static PolylineCurve ToPolyline(this Curve curve, double tolerance, double angleTolerance, double minimumLength, double maximumLength)
        {
            return ComputeServer.Post<PolylineCurve>(ApiAddress(), curve, tolerance, angleTolerance, minimumLength, maximumLength);
        }

        /// <summary>
        /// Gets a polyline approximation of a curve.
        /// </summary>
        /// <param name="tolerance">The tolerance. This is the maximum deviation from line midpoints to the curve. When in doubt, use the document's model space absolute tolerance.</param>
        /// <param name="angleTolerance">The angle tolerance in radians. This is the maximum deviation of the line directions. When in doubt, use the document's model space angle tolerance.</param>
        /// <param name="minimumLength">The minimum segment length.</param>
        /// <param name="maximumLength">The maximum segment length.</param>
        /// <returns>PolyCurve on success, null on error.</returns>
        public static PolylineCurve ToPolyline(Remote<Curve> curve, double tolerance, double angleTolerance, double minimumLength, double maximumLength)
        {
            return ComputeServer.Post<PolylineCurve>(ApiAddress(), curve, tolerance, angleTolerance, minimumLength, maximumLength);
        }

        /// <summary>
        /// Converts a curve into polycurve consisting of arc segments. Sections of the input curves that are nearly straight are converted to straight-line segments.
        /// </summary>
        /// <param name="tolerance">The tolerance. This is the maximum deviation from arc midpoints to the curve. When in doubt, use the document's model space absolute tolerance.</param>
        /// <param name="angleTolerance">The angle tolerance in radians. This is the maximum deviation of the arc end directions from the curve direction. When in doubt, use the document's model space angle tolerance.</param>
        /// <param name="minimumLength">The minimum segment length.</param>
        /// <param name="maximumLength">The maximum segment length.</param>
        /// <returns>PolyCurve on success, null on error.</returns>
        public static PolyCurve ToArcsAndLines(this Curve curve, double tolerance, double angleTolerance, double minimumLength, double maximumLength)
        {
            return ComputeServer.Post<PolyCurve>(ApiAddress(), curve, tolerance, angleTolerance, minimumLength, maximumLength);
        }

        /// <summary>
        /// Converts a curve into polycurve consisting of arc segments. Sections of the input curves that are nearly straight are converted to straight-line segments.
        /// </summary>
        /// <param name="tolerance">The tolerance. This is the maximum deviation from arc midpoints to the curve. When in doubt, use the document's model space absolute tolerance.</param>
        /// <param name="angleTolerance">The angle tolerance in radians. This is the maximum deviation of the arc end directions from the curve direction. When in doubt, use the document's model space angle tolerance.</param>
        /// <param name="minimumLength">The minimum segment length.</param>
        /// <param name="maximumLength">The maximum segment length.</param>
        /// <returns>PolyCurve on success, null on error.</returns>
        public static PolyCurve ToArcsAndLines(Remote<Curve> curve, double tolerance, double angleTolerance, double minimumLength, double maximumLength)
        {
            return ComputeServer.Post<PolyCurve>(ApiAddress(), curve, tolerance, angleTolerance, minimumLength, maximumLength);
        }

        /// <summary>
        /// Makes a polyline approximation of the curve and gets the closest point on the mesh for each point on the curve. 
        /// Then it "connects the points" so that you have a polyline on the mesh.
        /// </summary>
        /// <param name="mesh">Mesh to project onto.</param>
        /// <param name="tolerance">Input tolerance (RhinoDoc.ModelAbsoluteTolerance is a good default)</param>
        /// <returns>A polyline curve on success, null on failure.</returns>
        public static PolylineCurve PullToMesh(this Curve curve, Mesh mesh, double tolerance)
        {
            return ComputeServer.Post<PolylineCurve>(ApiAddress(), curve, mesh, tolerance);
        }

        /// <summary>
        /// Makes a polyline approximation of the curve and gets the closest point on the mesh for each point on the curve. 
        /// Then it "connects the points" so that you have a polyline on the mesh.
        /// </summary>
        /// <param name="mesh">Mesh to project onto.</param>
        /// <param name="tolerance">Input tolerance (RhinoDoc.ModelAbsoluteTolerance is a good default)</param>
        /// <returns>A polyline curve on success, null on failure.</returns>
        public static PolylineCurve PullToMesh(Remote<Curve> curve, Remote<Mesh> mesh, double tolerance)
        {
            return ComputeServer.Post<PolylineCurve>(ApiAddress(), curve, mesh, tolerance);
        }

        /// <summary>
        /// Offsets this curve. If you have a nice offset, then there will be one entry in 
        /// the array. If the original curve had kinks or the offset curve had self 
        /// intersections, you will get multiple segments in the output array.
        /// </summary>
        /// <param name="plane">Offset solution plane.</param>
        /// <param name="distance">The positive or negative distance to offset.</param>
        /// <param name="tolerance">The offset or fitting tolerance.</param>
        /// <param name="cornerStyle">Corner style for offset kinks.</param>
        /// <returns>Offset curves on success, null on failure.</returns>
        public static Curve[] Offset(this Curve curve, Plane plane, double distance, double tolerance, CurveOffsetCornerStyle cornerStyle)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, plane, distance, tolerance, cornerStyle);
        }

        /// <summary>
        /// Offsets this curve. If you have a nice offset, then there will be one entry in 
        /// the array. If the original curve had kinks or the offset curve had self 
        /// intersections, you will get multiple segments in the output array.
        /// </summary>
        /// <param name="plane">Offset solution plane.</param>
        /// <param name="distance">The positive or negative distance to offset.</param>
        /// <param name="tolerance">The offset or fitting tolerance.</param>
        /// <param name="cornerStyle">Corner style for offset kinks.</param>
        /// <returns>Offset curves on success, null on failure.</returns>
        public static Curve[] Offset(Remote<Curve> curve, Plane plane, double distance, double tolerance, CurveOffsetCornerStyle cornerStyle)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, plane, distance, tolerance, cornerStyle);
        }

        /// <summary>
        /// Offsets this curve. If you have a nice offset, then there will be one entry in 
        /// the array. If the original curve had kinks or the offset curve had self 
        /// intersections, you will get multiple segments in the output array.
        /// </summary>
        /// <param name="directionPoint">A point that indicates the direction of the offset.</param>
        /// <param name="normal">The normal to the offset plane.</param>
        /// <param name="distance">The positive or negative distance to offset.</param>
        /// <param name="tolerance">The offset or fitting tolerance.</param>
        /// <param name="cornerStyle">Corner style for offset kinks.</param>
        /// <returns>Offset curves on success, null on failure.</returns>
        public static Curve[] Offset(this Curve curve, Point3d directionPoint, Vector3d normal, double distance, double tolerance, CurveOffsetCornerStyle cornerStyle)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, directionPoint, normal, distance, tolerance, cornerStyle);
        }

        /// <summary>
        /// Offsets this curve. If you have a nice offset, then there will be one entry in 
        /// the array. If the original curve had kinks or the offset curve had self 
        /// intersections, you will get multiple segments in the output array.
        /// </summary>
        /// <param name="directionPoint">A point that indicates the direction of the offset.</param>
        /// <param name="normal">The normal to the offset plane.</param>
        /// <param name="distance">The positive or negative distance to offset.</param>
        /// <param name="tolerance">The offset or fitting tolerance.</param>
        /// <param name="cornerStyle">Corner style for offset kinks.</param>
        /// <returns>Offset curves on success, null on failure.</returns>
        public static Curve[] Offset(Remote<Curve> curve, Point3d directionPoint, Vector3d normal, double distance, double tolerance, CurveOffsetCornerStyle cornerStyle)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, directionPoint, normal, distance, tolerance, cornerStyle);
        }

        /// <summary>
        /// Offsets this curve. If you have a nice offset, then there will be one entry in 
        /// the array. If the original curve had kinks or the offset curve had self 
        /// intersections, you will get multiple segments in the output array.
        /// </summary>
        /// <param name="directionPoint">A point that indicates the direction of the offset.</param>
        /// <param name="normal">The normal to the offset plane.</param>
        /// <param name="distance">The positive or negative distance to offset.</param>
        /// <param name="tolerance">The offset or fitting tolerance.</param>
        /// <param name="angleTolerance">The angle tolerance, in radians, used to decide whether to split at kinks.</param>
        /// <param name="loose">If false, offset within tolerance. If true, offset by moving edit points.</param>
        /// <param name="cornerStyle">Corner style for offset kinks.</param>
        /// <param name="endStyle">End style for non-loose, non-closed curve offsets.</param>
        /// <returns>Offset curves on success, null on failure.</returns>
        public static Curve[] Offset(this Curve curve, Point3d directionPoint, Vector3d normal, double distance, double tolerance, double angleTolerance, bool loose, CurveOffsetCornerStyle cornerStyle, CurveOffsetEndStyle endStyle)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, directionPoint, normal, distance, tolerance, angleTolerance, loose, cornerStyle, endStyle);
        }

        /// <summary>
        /// Offsets this curve. If you have a nice offset, then there will be one entry in 
        /// the array. If the original curve had kinks or the offset curve had self 
        /// intersections, you will get multiple segments in the output array.
        /// </summary>
        /// <param name="directionPoint">A point that indicates the direction of the offset.</param>
        /// <param name="normal">The normal to the offset plane.</param>
        /// <param name="distance">The positive or negative distance to offset.</param>
        /// <param name="tolerance">The offset or fitting tolerance.</param>
        /// <param name="angleTolerance">The angle tolerance, in radians, used to decide whether to split at kinks.</param>
        /// <param name="loose">If false, offset within tolerance. If true, offset by moving edit points.</param>
        /// <param name="cornerStyle">Corner style for offset kinks.</param>
        /// <param name="endStyle">End style for non-loose, non-closed curve offsets.</param>
        /// <returns>Offset curves on success, null on failure.</returns>
        public static Curve[] Offset(Remote<Curve> curve, Point3d directionPoint, Vector3d normal, double distance, double tolerance, double angleTolerance, bool loose, CurveOffsetCornerStyle cornerStyle, CurveOffsetEndStyle endStyle)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, directionPoint, normal, distance, tolerance, angleTolerance, loose, cornerStyle, endStyle);
        }

        /// <summary>
        /// Offsets a closed curve in the following way: pProject the curve to a plane with given normal.
        /// Then, loose Offset the projection by distance + blend_radius and trim off self-intersection.
        /// THen, Offset the remaining curve back in the opposite direction by blend_radius, filling gaps with blends.
        /// Finally, use the elevations of the input curve to get the correct elevations of the result.
        /// </summary>
        /// <param name="distance">The positive distance to offset the curve.</param>
        /// <param name="blendRadius">
        /// Positive, typically the same as distance. When the offset results in a self-intersection
        /// that gets trimmed off at a kink, the kink will be blended out using this radius.
        /// </param>
        /// <param name="directionPoint">
        /// A point that indicates the direction of the offset. If the offset is inward,
        /// the point's projection to the plane should be well within the curve.
        /// It will be used to decide which part of the offset to keep if there are self-intersections.
        /// </param>
        /// <param name="normal">A vector that indicates the normal of the plane in which the offset will occur.</param>
        /// <param name="tolerance">Used to determine self-intersections, not offset error.</param>
        /// <returns>The offset curve if successful.</returns>
        public static Curve RibbonOffset(this Curve curve, out Curve updatedInstance, double distance, double blendRadius, Point3d directionPoint, Vector3d normal, double tolerance)
        {
            return ComputeServer.Post<Curve, Curve>(ApiAddress(), out updatedInstance, curve, distance, blendRadius, directionPoint, normal, tolerance);
        }

        /// <summary>
        /// Offsets a closed curve in the following way: pProject the curve to a plane with given normal.
        /// Then, loose Offset the projection by distance + blend_radius and trim off self-intersection.
        /// THen, Offset the remaining curve back in the opposite direction by blend_radius, filling gaps with blends.
        /// Finally, use the elevations of the input curve to get the correct elevations of the result.
        /// </summary>
        /// <param name="distance">The positive distance to offset the curve.</param>
        /// <param name="blendRadius">
        /// Positive, typically the same as distance. When the offset results in a self-intersection
        /// that gets trimmed off at a kink, the kink will be blended out using this radius.
        /// </param>
        /// <param name="directionPoint">
        /// A point that indicates the direction of the offset. If the offset is inward,
        /// the point's projection to the plane should be well within the curve.
        /// It will be used to decide which part of the offset to keep if there are self-intersections.
        /// </param>
        /// <param name="normal">A vector that indicates the normal of the plane in which the offset will occur.</param>
        /// <param name="tolerance">Used to determine self-intersections, not offset error.</param>
        /// <returns>The offset curve if successful.</returns>
        public static Curve RibbonOffset(Remote<Curve> curve, out Curve updatedInstance, double distance, double blendRadius, Point3d directionPoint, Vector3d normal, double tolerance)
        {
            return ComputeServer.Post<Curve, Curve>(ApiAddress(), out updatedInstance, curve, distance, blendRadius, directionPoint, normal, tolerance);
        }

        /// <summary>
        /// Offset this curve on a brep face surface. This curve must lie on the surface.
        /// </summary>
        /// <param name="face">The brep face on which to offset.</param>
        /// <param name="distance">A distance to offset (+)left, (-)right.</param>
        /// <param name="fittingTolerance">A fitting tolerance.</param>
        /// <returns>Offset curves on success, or null on failure.</returns>
        /// <exception cref="ArgumentNullException">If face is null.</exception>
        public static Curve[] OffsetOnSurface(this Curve curve, BrepFace face, double distance, double fittingTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, face, distance, fittingTolerance);
        }

        /// <summary>
        /// Offset this curve on a brep face surface. This curve must lie on the surface.
        /// </summary>
        /// <param name="face">The brep face on which to offset.</param>
        /// <param name="distance">A distance to offset (+)left, (-)right.</param>
        /// <param name="fittingTolerance">A fitting tolerance.</param>
        /// <returns>Offset curves on success, or null on failure.</returns>
        /// <exception cref="ArgumentNullException">If face is null.</exception>
        public static Curve[] OffsetOnSurface(Remote<Curve> curve, BrepFace face, double distance, double fittingTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, face, distance, fittingTolerance);
        }

        /// <summary>
        /// Offset a curve on a brep face surface. This curve must lie on the surface.
        /// <para>This overload allows to specify a surface point at which the offset will pass.</para>
        /// </summary>
        /// <param name="face">The brep face on which to offset.</param>
        /// <param name="throughPoint">2d point on the brep face to offset through.</param>
        /// <param name="fittingTolerance">A fitting tolerance.</param>
        /// <returns>Offset curves on success, or null on failure.</returns>
        /// <exception cref="ArgumentNullException">If face is null.</exception>
        public static Curve[] OffsetOnSurface(this Curve curve, BrepFace face, Point2d throughPoint, double fittingTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, face, throughPoint, fittingTolerance);
        }

        /// <summary>
        /// Offset a curve on a brep face surface. This curve must lie on the surface.
        /// <para>This overload allows to specify a surface point at which the offset will pass.</para>
        /// </summary>
        /// <param name="face">The brep face on which to offset.</param>
        /// <param name="throughPoint">2d point on the brep face to offset through.</param>
        /// <param name="fittingTolerance">A fitting tolerance.</param>
        /// <returns>Offset curves on success, or null on failure.</returns>
        /// <exception cref="ArgumentNullException">If face is null.</exception>
        public static Curve[] OffsetOnSurface(Remote<Curve> curve, BrepFace face, Point2d throughPoint, double fittingTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, face, throughPoint, fittingTolerance);
        }

        /// <summary>
        /// Offset a curve on a brep face surface. This curve must lie on the surface.
        /// <para>This overload allows to specify different offsets for different curve parameters.</para>
        /// </summary>
        /// <param name="face">The brep face on which to offset.</param>
        /// <param name="curveParameters">Curve parameters corresponding to the offset distances.</param>
        /// <param name="offsetDistances">distances to offset (+)left, (-)right.</param>
        /// <param name="fittingTolerance">A fitting tolerance.</param>
        /// <returns>Offset curves on success, or null on failure.</returns>
        /// <exception cref="ArgumentNullException">If face, curveParameters or offsetDistances are null.</exception>
        public static Curve[] OffsetOnSurface(this Curve curve, BrepFace face, double[] curveParameters, double[] offsetDistances, double fittingTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, face, curveParameters, offsetDistances, fittingTolerance);
        }

        /// <summary>
        /// Offset a curve on a brep face surface. This curve must lie on the surface.
        /// <para>This overload allows to specify different offsets for different curve parameters.</para>
        /// </summary>
        /// <param name="face">The brep face on which to offset.</param>
        /// <param name="curveParameters">Curve parameters corresponding to the offset distances.</param>
        /// <param name="offsetDistances">distances to offset (+)left, (-)right.</param>
        /// <param name="fittingTolerance">A fitting tolerance.</param>
        /// <returns>Offset curves on success, or null on failure.</returns>
        /// <exception cref="ArgumentNullException">If face, curveParameters or offsetDistances are null.</exception>
        public static Curve[] OffsetOnSurface(Remote<Curve> curve, BrepFace face, double[] curveParameters, double[] offsetDistances, double fittingTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, face, curveParameters, offsetDistances, fittingTolerance);
        }

        /// <summary>
        /// Offset a curve on a surface. This curve must lie on the surface.
        /// </summary>
        /// <param name="surface">A surface on which to offset.</param>
        /// <param name="distance">A distance to offset (+)left, (-)right.</param>
        /// <param name="fittingTolerance">A fitting tolerance.</param>
        /// <returns>Offset curves on success, or null on failure.</returns>
        /// <exception cref="ArgumentNullException">If surface is null.</exception>
        public static Curve[] OffsetOnSurface(this Curve curve, Surface surface, double distance, double fittingTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, surface, distance, fittingTolerance);
        }

        /// <summary>
        /// Offset a curve on a surface. This curve must lie on the surface.
        /// </summary>
        /// <param name="surface">A surface on which to offset.</param>
        /// <param name="distance">A distance to offset (+)left, (-)right.</param>
        /// <param name="fittingTolerance">A fitting tolerance.</param>
        /// <returns>Offset curves on success, or null on failure.</returns>
        /// <exception cref="ArgumentNullException">If surface is null.</exception>
        public static Curve[] OffsetOnSurface(Remote<Curve> curve, Remote<Surface> surface, double distance, double fittingTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, surface, distance, fittingTolerance);
        }

        /// <summary>
        /// Offset a curve on a surface. This curve must lie on the surface.
        /// <para>This overload allows to specify a surface point at which the offset will pass.</para>
        /// </summary>
        /// <param name="surface">A surface on which to offset.</param>
        /// <param name="throughPoint">2d point on the brep face to offset through.</param>
        /// <param name="fittingTolerance">A fitting tolerance.</param>
        /// <returns>Offset curves on success, or null on failure.</returns>
        /// <exception cref="ArgumentNullException">If surface is null.</exception>
        public static Curve[] OffsetOnSurface(this Curve curve, Surface surface, Point2d throughPoint, double fittingTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, surface, throughPoint, fittingTolerance);
        }

        /// <summary>
        /// Offset a curve on a surface. This curve must lie on the surface.
        /// <para>This overload allows to specify a surface point at which the offset will pass.</para>
        /// </summary>
        /// <param name="surface">A surface on which to offset.</param>
        /// <param name="throughPoint">2d point on the brep face to offset through.</param>
        /// <param name="fittingTolerance">A fitting tolerance.</param>
        /// <returns>Offset curves on success, or null on failure.</returns>
        /// <exception cref="ArgumentNullException">If surface is null.</exception>
        public static Curve[] OffsetOnSurface(Remote<Curve> curve, Remote<Surface> surface, Point2d throughPoint, double fittingTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, surface, throughPoint, fittingTolerance);
        }

        /// <summary>
        /// Offset this curve on a surface. This curve must lie on the surface.
        /// <para>This overload allows to specify different offsets for different curve parameters.</para>
        /// </summary>
        /// <param name="surface">A surface on which to offset.</param>
        /// <param name="curveParameters">Curve parameters corresponding to the offset distances.</param>
        /// <param name="offsetDistances">Distances to offset (+)left, (-)right.</param>
        /// <param name="fittingTolerance">A fitting tolerance.</param>
        /// <returns>Offset curves on success, or null on failure.</returns>
        /// <exception cref="ArgumentNullException">If surface, curveParameters or offsetDistances are null.</exception>
        public static Curve[] OffsetOnSurface(this Curve curve, Surface surface, double[] curveParameters, double[] offsetDistances, double fittingTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, surface, curveParameters, offsetDistances, fittingTolerance);
        }

        /// <summary>
        /// Offset this curve on a surface. This curve must lie on the surface.
        /// <para>This overload allows to specify different offsets for different curve parameters.</para>
        /// </summary>
        /// <param name="surface">A surface on which to offset.</param>
        /// <param name="curveParameters">Curve parameters corresponding to the offset distances.</param>
        /// <param name="offsetDistances">Distances to offset (+)left, (-)right.</param>
        /// <param name="fittingTolerance">A fitting tolerance.</param>
        /// <returns>Offset curves on success, or null on failure.</returns>
        /// <exception cref="ArgumentNullException">If surface, curveParameters or offsetDistances are null.</exception>
        public static Curve[] OffsetOnSurface(Remote<Curve> curve, Remote<Surface> surface, double[] curveParameters, double[] offsetDistances, double fittingTolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, surface, curveParameters, offsetDistances, fittingTolerance);
        }

        /// <summary>
        /// Pulls this curve to a brep face and returns the result of that operation.
        /// </summary>
        /// <param name="face">A brep face.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>An array containing the resulting curves after pulling. This array could be empty.</returns>
        /// <exception cref="ArgumentNullException">If face is null.</exception>
        public static Curve[] PullToBrepFace(this Curve curve, BrepFace face, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, face, tolerance);
        }

        /// <summary>
        /// Pulls this curve to a brep face and returns the result of that operation.
        /// </summary>
        /// <param name="face">A brep face.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>An array containing the resulting curves after pulling. This array could be empty.</returns>
        /// <exception cref="ArgumentNullException">If face is null.</exception>
        public static Curve[] PullToBrepFace(Remote<Curve> curve, BrepFace face, double tolerance)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), curve, face, tolerance);
        }

        /// <summary>
        /// Finds a curve by offsetting an existing curve normal to a surface.
        /// The caller is responsible for ensuring that the curve lies on the input surface.
        /// </summary>
        /// <param name="surface">Surface from which normals are calculated.</param>
        /// <param name="height">offset distance (distance from surface to result curve)</param>
        /// <returns>
        /// Offset curve at distance height from the surface.  The offset curve is
        /// interpolated through a small number of points so if the surface is irregular
        /// or complicated, the result will not be a very accurate offset.
        /// </returns>
        public static Curve OffsetNormalToSurface(this Curve curve, Surface surface, double height)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, surface, height);
        }

        /// <summary>
        /// Finds a curve by offsetting an existing curve normal to a surface.
        /// The caller is responsible for ensuring that the curve lies on the input surface.
        /// </summary>
        /// <param name="surface">Surface from which normals are calculated.</param>
        /// <param name="height">offset distance (distance from surface to result curve)</param>
        /// <returns>
        /// Offset curve at distance height from the surface.  The offset curve is
        /// interpolated through a small number of points so if the surface is irregular
        /// or complicated, the result will not be a very accurate offset.
        /// </returns>
        public static Curve OffsetNormalToSurface(Remote<Curve> curve, Remote<Surface> surface, double height)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), curve, surface, height);
        }
    }

    public static class ExtrusionCompute
    {
        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return ComputeServer.ApiAddress(typeof(Extrusion), caller);
        }

        /// <summary>
        /// Constructs all the Wireframe curves for this Extrusion.
        /// </summary>
        /// <returns>An array of Wireframe curves.</returns>
        public static Curve[] GetWireframe(this Extrusion extrusion)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), extrusion);
        }

        /// <summary>
        /// Constructs all the Wireframe curves for this Extrusion.
        /// </summary>
        /// <returns>An array of Wireframe curves.</returns>
        public static Curve[] GetWireframe(Remote<Extrusion> extrusion)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), extrusion);
        }
    }

    public static class BezierCurveCompute
    {
        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return ComputeServer.ApiAddress(typeof(BezierCurve), caller);
        }

        /// <summary>
        /// Constructs an array of cubic, non-rational Beziers that fit a curve to a tolerance.
        /// </summary>
        /// <param name="sourceCurve">A curve to approximate.</param>
        /// <param name="distanceTolerance">
        /// The max fitting error. Use RhinoMath.SqrtEpsilon as a minimum.
        /// </param>
        /// <param name="kinkTolerance">
        /// If the input curve has a g1-discontinuity with angle radian measure
        /// greater than kinkTolerance at some point P, the list of beziers will
        /// also have a kink at P.
        /// </param>
        /// <returns>A new array of bezier curves. The array can be empty and might contain null items.</returns>
        public static BezierCurve[] CreateCubicBeziers(Curve sourceCurve, double distanceTolerance, double kinkTolerance)
        {
            return ComputeServer.Post<BezierCurve[]>(ApiAddress(), sourceCurve, distanceTolerance, kinkTolerance);
        }

        /// <summary>
        /// Constructs an array of cubic, non-rational Beziers that fit a curve to a tolerance.
        /// </summary>
        /// <param name="sourceCurve">A curve to approximate.</param>
        /// <param name="distanceTolerance">
        /// The max fitting error. Use RhinoMath.SqrtEpsilon as a minimum.
        /// </param>
        /// <param name="kinkTolerance">
        /// If the input curve has a g1-discontinuity with angle radian measure
        /// greater than kinkTolerance at some point P, the list of beziers will
        /// also have a kink at P.
        /// </param>
        /// <returns>A new array of bezier curves. The array can be empty and might contain null items.</returns>
        public static BezierCurve[] CreateCubicBeziers(Remote<Curve> sourceCurve, double distanceTolerance, double kinkTolerance)
        {
            return ComputeServer.Post<BezierCurve[]>(ApiAddress(), sourceCurve, distanceTolerance, kinkTolerance);
        }

        /// <summary>
        /// Create an array of Bezier curves that fit to an existing curve. Please note, these
        /// Beziers can be of any order and may be rational.
        /// </summary>
        /// <param name="sourceCurve">The curve to fit Beziers to</param>
        /// <returns>A new array of Bezier curves</returns>
        public static BezierCurve[] CreateBeziers(Curve sourceCurve)
        {
            return ComputeServer.Post<BezierCurve[]>(ApiAddress(), sourceCurve);
        }

        /// <summary>
        /// Create an array of Bezier curves that fit to an existing curve. Please note, these
        /// Beziers can be of any order and may be rational.
        /// </summary>
        /// <param name="sourceCurve">The curve to fit Beziers to</param>
        /// <returns>A new array of Bezier curves</returns>
        public static BezierCurve[] CreateBeziers(Remote<Curve> sourceCurve)
        {
            return ComputeServer.Post<BezierCurve[]>(ApiAddress(), sourceCurve);
        }
    }

    public static class BrepCompute
    {
        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return ComputeServer.ApiAddress(typeof(Brep), caller);
        }

        /// <summary>
        /// Change the seam of a closed trimmed surface.
        /// </summary>
        /// <param name="face">A Brep face with a closed underlying surface.</param>
        /// <param name="direction">The parameter direction (0 = U, 1 = V). The face's underlying surface must be closed in this direction.</param>
        /// <param name="parameter">The parameter at which to place the seam.</param>
        /// <param name="tolerance">Tolerance used to cut up surface.</param>
        /// <returns>A new Brep that has the same geometry as the face with a relocated seam if successful, or null on failure.</returns>
        public static Brep ChangeSeam(BrepFace face, int direction, double parameter, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), face, direction, parameter, tolerance);
        }

        /// <summary>
        /// Copy all trims from a Brep face onto a surface.
        /// </summary>
        /// <param name="trimSource">Brep face which defines the trimming curves.</param>
        /// <param name="surfaceSource">The surface to trim.</param>
        /// <param name="tolerance">Tolerance to use for rebuilding 3D trim curves.</param>
        /// <returns>A brep with the shape of surfaceSource and the trims of trimSource or null on failure.</returns>
        public static Brep CopyTrimCurves(BrepFace trimSource, Surface surfaceSource, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), trimSource, surfaceSource, tolerance);
        }

        /// <summary>
        /// Copy all trims from a Brep face onto a surface.
        /// </summary>
        /// <param name="trimSource">Brep face which defines the trimming curves.</param>
        /// <param name="surfaceSource">The surface to trim.</param>
        /// <param name="tolerance">Tolerance to use for rebuilding 3D trim curves.</param>
        /// <returns>A brep with the shape of surfaceSource and the trims of trimSource or null on failure.</returns>
        public static Brep CopyTrimCurves(BrepFace trimSource, Remote<Surface> surfaceSource, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), trimSource, surfaceSource, tolerance);
        }

        /// <summary>
        /// Creates a brep representation of the sphere with two similar trimmed NURBS surfaces, and no singularities.
        /// </summary>
        /// <param name="center">The center of the sphere.</param>
        /// <param name="radius">The radius of the sphere.</param>
        /// <param name="tolerance">
        /// Used in computing 2d trimming curves. If &gt;= 0.0, then the max of 
        /// ON_0.0001 * radius and RhinoMath.ZeroTolerance will be used.
        /// </param>
        /// <returns>A new brep, or null on error.</returns>
        public static Brep CreateBaseballSphere(Point3d center, double radius, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), center, radius, tolerance);
        }

        /// <summary>
        /// Creates a single developable surface between two curves.
        /// </summary>
        /// <param name="crv0">The first rail curve.</param>
        /// <param name="crv1">The second rail curve.</param>
        /// <param name="reverse0">Reverse the first rail curve.</param>
        /// <param name="reverse1">Reverse the second rail curve</param>
        /// <param name="density">The number of rulings across the surface.</param>
        /// <returns>The output Breps if successful, otherwise an empty array.</returns>
        public static Brep[] CreateDevelopableLoft(Curve crv0, Curve crv1, bool reverse0, bool reverse1, int density)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), crv0, crv1, reverse0, reverse1, density);
        }

        /// <summary>
        /// Creates a single developable surface between two curves.
        /// </summary>
        /// <param name="crv0">The first rail curve.</param>
        /// <param name="crv1">The second rail curve.</param>
        /// <param name="reverse0">Reverse the first rail curve.</param>
        /// <param name="reverse1">Reverse the second rail curve</param>
        /// <param name="density">The number of rulings across the surface.</param>
        /// <returns>The output Breps if successful, otherwise an empty array.</returns>
        public static Brep[] CreateDevelopableLoft(Remote<Curve> crv0, Remote<Curve> crv1, bool reverse0, bool reverse1, int density)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), crv0, crv1, reverse0, reverse1, density);
        }

        /// <summary>
        /// Creates a single developable surface between two curves.
        /// </summary>
        /// <param name="rail0">The first rail curve.</param>
        /// <param name="rail1">The second rail curve.</param>
        /// <param name="fixedRulings">
        /// Rulings define lines across the surface that define the straight sections on the developable surface,
        /// where rulings[i].X = parameter on first rail curve, and rulings[i].Y = parameter on second rail curve.
        /// Note, rulings will be automatically adjusted to minimum twist.
        /// </param>
        /// <returns>The output Breps if successful, otherwise an empty array.</returns>
        public static Brep[] CreateDevelopableLoft(NurbsCurve rail0, NurbsCurve rail1, IEnumerable<Point2d> fixedRulings)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail0, rail1, fixedRulings);
        }

        /// <summary>
        /// Creates a single developable surface between two curves.
        /// </summary>
        /// <param name="rail0">The first rail curve.</param>
        /// <param name="rail1">The second rail curve.</param>
        /// <param name="fixedRulings">
        /// Rulings define lines across the surface that define the straight sections on the developable surface,
        /// where rulings[i].X = parameter on first rail curve, and rulings[i].Y = parameter on second rail curve.
        /// Note, rulings will be automatically adjusted to minimum twist.
        /// </param>
        /// <returns>The output Breps if successful, otherwise an empty array.</returns>
        public static Brep[] CreateDevelopableLoft(Remote<NurbsCurve> rail0, Remote<NurbsCurve> rail1, IEnumerable<Point2d> fixedRulings)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail0, rail1, fixedRulings);
        }

        /// <summary>
        /// Constructs a set of planar breps as outlines by the loops.
        /// </summary>
        /// <param name="inputLoops">Curve loops that delineate the planar boundaries.</param>
        /// <returns>An array of Planar Breps.</returns>
        /// <deprecated>6.0</deprecated>
        public static Brep[] CreatePlanarBreps(IEnumerable<Curve> inputLoops)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), inputLoops);
        }

        /// <summary>
        /// Constructs a set of planar breps as outlines by the loops.
        /// </summary>
        /// <param name="inputLoops">Curve loops that delineate the planar boundaries.</param>
        /// <returns>An array of Planar Breps.</returns>
        /// <deprecated>6.0</deprecated>
        public static Brep[] CreatePlanarBreps(Remote<IEnumerable<Curve>> inputLoops)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), inputLoops);
        }

        /// <summary>
        /// Constructs a set of planar breps as outlines by the loops.
        /// </summary>
        /// <param name="inputLoops">Curve loops that delineate the planar boundaries.</param>
        /// <param name="tolerance"></param>
        /// <returns>An array of Planar Breps.</returns>
        public static Brep[] CreatePlanarBreps(IEnumerable<Curve> inputLoops, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), inputLoops, tolerance);
        }

        /// <summary>
        /// Constructs a set of planar breps as outlines by the loops.
        /// </summary>
        /// <param name="inputLoops">Curve loops that delineate the planar boundaries.</param>
        /// <param name="tolerance"></param>
        /// <returns>An array of Planar Breps.</returns>
        public static Brep[] CreatePlanarBreps(Remote<IEnumerable<Curve>> inputLoops, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), inputLoops, tolerance);
        }

        /// <summary>
        /// Constructs a set of planar breps as outlines by the loops.
        /// </summary>
        /// <param name="inputLoop">A curve that should form the boundaries of the surfaces or polysurfaces.</param>
        /// <returns>An array of Planar Breps.</returns>
        /// <deprecated>6.0</deprecated>
        public static Brep[] CreatePlanarBreps(Curve inputLoop)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), inputLoop);
        }

        /// <summary>
        /// Constructs a set of planar breps as outlines by the loops.
        /// </summary>
        /// <param name="inputLoop">A curve that should form the boundaries of the surfaces or polysurfaces.</param>
        /// <returns>An array of Planar Breps.</returns>
        /// <deprecated>6.0</deprecated>
        public static Brep[] CreatePlanarBreps(Remote<Curve> inputLoop)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), inputLoop);
        }

        /// <summary>
        /// Constructs a set of planar breps as outlines by the loops.
        /// </summary>
        /// <param name="inputLoop">A curve that should form the boundaries of the surfaces or polysurfaces.</param>
        /// <param name="tolerance"></param>
        /// <returns>An array of Planar Breps.</returns>
        public static Brep[] CreatePlanarBreps(Curve inputLoop, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), inputLoop, tolerance);
        }

        /// <summary>
        /// Constructs a set of planar breps as outlines by the loops.
        /// </summary>
        /// <param name="inputLoop">A curve that should form the boundaries of the surfaces or polysurfaces.</param>
        /// <param name="tolerance"></param>
        /// <returns>An array of Planar Breps.</returns>
        public static Brep[] CreatePlanarBreps(Remote<Curve> inputLoop, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), inputLoop, tolerance);
        }

        /// <summary>
        /// Constructs a Brep using the trimming information of a brep face and a surface. 
        /// Surface must be roughly the same shape and in the same location as the trimming brep face.
        /// </summary>
        /// <param name="trimSource">BrepFace which contains trimmingSource brep.</param>
        /// <param name="surfaceSource">Surface that trims of BrepFace will be applied to.</param>
        /// <returns>A brep with the shape of surfaceSource and the trims of trimSource or null on failure.</returns>
        /// <deprecated>6.0</deprecated>
        public static Brep CreateTrimmedSurface(BrepFace trimSource, Surface surfaceSource)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), trimSource, surfaceSource);
        }

        /// <summary>
        /// Constructs a Brep using the trimming information of a brep face and a surface. 
        /// Surface must be roughly the same shape and in the same location as the trimming brep face.
        /// </summary>
        /// <param name="trimSource">BrepFace which contains trimmingSource brep.</param>
        /// <param name="surfaceSource">Surface that trims of BrepFace will be applied to.</param>
        /// <returns>A brep with the shape of surfaceSource and the trims of trimSource or null on failure.</returns>
        /// <deprecated>6.0</deprecated>
        public static Brep CreateTrimmedSurface(BrepFace trimSource, Remote<Surface> surfaceSource)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), trimSource, surfaceSource);
        }

        /// <summary>
        /// Constructs a Brep using the trimming information of a brep face and a surface. 
        /// Surface must be roughly the same shape and in the same location as the trimming brep face.
        /// </summary>
        /// <param name="trimSource">BrepFace which contains trimmingSource brep.</param>
        /// <param name="surfaceSource">Surface that trims of BrepFace will be applied to.</param>
        /// <param name="tolerance"></param>
        /// <returns>A brep with the shape of surfaceSource and the trims of trimSource or null on failure.</returns>
        public static Brep CreateTrimmedSurface(BrepFace trimSource, Surface surfaceSource, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), trimSource, surfaceSource, tolerance);
        }

        /// <summary>
        /// Constructs a Brep using the trimming information of a brep face and a surface. 
        /// Surface must be roughly the same shape and in the same location as the trimming brep face.
        /// </summary>
        /// <param name="trimSource">BrepFace which contains trimmingSource brep.</param>
        /// <param name="surfaceSource">Surface that trims of BrepFace will be applied to.</param>
        /// <param name="tolerance"></param>
        /// <returns>A brep with the shape of surfaceSource and the trims of trimSource or null on failure.</returns>
        public static Brep CreateTrimmedSurface(BrepFace trimSource, Remote<Surface> surfaceSource, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), trimSource, surfaceSource, tolerance);
        }

        /// <summary>
        /// Makes a Brep with one face from three corner points.
        /// </summary>
        /// <param name="corner1">A first corner.</param>
        /// <param name="corner2">A second corner.</param>
        /// <param name="corner3">A third corner.</param>
        /// <param name="tolerance">
        /// Minimum edge length allowed before collapsing the side into a singularity.
        /// </param>
        /// <returns>A boundary representation, or null on error.</returns>
        public static Brep CreateFromCornerPoints(Point3d corner1, Point3d corner2, Point3d corner3, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), corner1, corner2, corner3, tolerance);
        }

        /// <summary>
        /// Makes a Brep with one face from four corner points.
        /// </summary>
        /// <param name="corner1">A first corner.</param>
        /// <param name="corner2">A second corner.</param>
        /// <param name="corner3">A third corner.</param>
        /// <param name="corner4">A fourth corner.</param>
        /// <param name="tolerance">
        /// Minimum edge length allowed before collapsing the side into a singularity.
        /// </param>
        /// <returns>A boundary representation, or null on error.</returns>
        public static Brep CreateFromCornerPoints(Point3d corner1, Point3d corner2, Point3d corner3, Point3d corner4, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), corner1, corner2, corner3, corner4, tolerance);
        }

        /// <summary>
        /// Constructs a coons patch from 2, 3, or 4 curves.
        /// </summary>
        /// <param name="curves">A list, an array or any enumerable set of curves.</param>
        /// <returns>resulting brep or null on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_edgesrf.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_edgesrf.cs' lang='cs'/>
        /// <code source='examples\py\ex_edgesrf.py' lang='py'/>
        /// </example>
        public static Brep CreateEdgeSurface(IEnumerable<Curve> curves)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), curves);
        }

        /// <summary>
        /// Constructs a coons patch from 2, 3, or 4 curves.
        /// </summary>
        /// <param name="curves">A list, an array or any enumerable set of curves.</param>
        /// <returns>resulting brep or null on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_edgesrf.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_edgesrf.cs' lang='cs'/>
        /// <code source='examples\py\ex_edgesrf.py' lang='py'/>
        /// </example>
        public static Brep CreateEdgeSurface(Remote<IEnumerable<Curve>> curves)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), curves);
        }

        /// <summary>
        /// Constructs a set of planar Breps as outlines by the loops.
        /// </summary>
        /// <param name="inputLoops">Curve loops that delineate the planar boundaries.</param>
        /// <returns>An array of Planar Breps or null on error.</returns>
        /// <deprecated>6.0</deprecated>
        public static Brep[] CreatePlanarBreps(Rhino.Collections.CurveList inputLoops)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), inputLoops);
        }

        /// <summary>
        /// Constructs a set of planar Breps as outlines by the loops.
        /// </summary>
        /// <param name="inputLoops">Curve loops that delineate the planar boundaries.</param>
        /// <param name="tolerance"></param>
        /// <returns>An array of Planar Breps.</returns>
        public static Brep[] CreatePlanarBreps(Rhino.Collections.CurveList inputLoops, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), inputLoops, tolerance);
        }

        /// <summary>
        /// Offsets a face including trim information to create a new brep.
        /// </summary>
        /// <param name="face">the face to offset.</param>
        /// <param name="offsetDistance">An offset distance.</param>
        /// <param name="offsetTolerance">
        ///  Use 0.0 to make a loose offset. Otherwise, the document's absolute tolerance is usually sufficient.
        /// </param>
        /// <param name="bothSides">When true, offset to both sides of the input face.</param>
        /// <param name="createSolid">When true, make a solid object.</param>
        /// <returns>
        /// A new brep if successful. The brep can be disjoint if bothSides is true and createSolid is false,
        /// or if createSolid is true and connecting the offsets with side surfaces fails.
        /// null if unsuccessful.
        /// </returns>
        public static Brep CreateFromOffsetFace(BrepFace face, double offsetDistance, double offsetTolerance, bool bothSides, bool createSolid)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), face, offsetDistance, offsetTolerance, bothSides, createSolid);
        }

        /// <summary>
        /// Constructs closed polysurfaces from surfaces and polysurfaces that bound a region in space.
        /// </summary>
        /// <param name="breps">
        /// The intersecting surfaces and polysurfaces to automatically trim and join into closed polysurfaces.
        /// </param>
        /// <param name="tolerance">
        /// The trim and join tolerance. If set to RhinoMath.UnsetValue, Rhino's global absolute tolerance is used.
        /// </param>
        /// <returns>The resulting polysurfaces on success or null on failure.</returns>
        public static Brep[] CreateSolid(IEnumerable<Brep> breps, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), breps, tolerance);
        }

        /// <summary>
        /// Constructs closed polysurfaces from surfaces and polysurfaces that bound a region in space.
        /// </summary>
        /// <param name="breps">
        /// The intersecting surfaces and polysurfaces to automatically trim and join into closed polysurfaces.
        /// </param>
        /// <param name="tolerance">
        /// The trim and join tolerance. If set to RhinoMath.UnsetValue, Rhino's global absolute tolerance is used.
        /// </param>
        /// <returns>The resulting polysurfaces on success or null on failure.</returns>
        public static Brep[] CreateSolid(Remote<IEnumerable<Brep>> breps, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), breps, tolerance);
        }

        /// <summary>
        /// Merges two surfaces into one surface at untrimmed edges.
        /// </summary>
        /// <param name="surface0">The first surface to merge.</param>
        /// <param name="surface1">The second surface to merge.</param>
        /// <param name="tolerance">Surface edges must be within this tolerance for the two surfaces to merge.</param>
        /// <param name="angleToleranceRadians">Edge must be within this angle tolerance in order for contiguous edges to be combined into a single edge.</param>
        /// <returns>The merged surfaces as a Brep if successful, null if not successful.</returns>
        public static Brep MergeSurfaces(Surface surface0, Surface surface1, double tolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), surface0, surface1, tolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Merges two surfaces into one surface at untrimmed edges.
        /// </summary>
        /// <param name="surface0">The first surface to merge.</param>
        /// <param name="surface1">The second surface to merge.</param>
        /// <param name="tolerance">Surface edges must be within this tolerance for the two surfaces to merge.</param>
        /// <param name="angleToleranceRadians">Edge must be within this angle tolerance in order for contiguous edges to be combined into a single edge.</param>
        /// <returns>The merged surfaces as a Brep if successful, null if not successful.</returns>
        public static Brep MergeSurfaces(Remote<Surface> surface0, Remote<Surface> surface1, double tolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), surface0, surface1, tolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Merges two surfaces into one surface at untrimmed edges. Both surfaces must be untrimmed and share an edge.
        /// </summary>
        /// <param name="brep0">The first single-face Brep to merge.</param>
        /// <param name="brep1">The second single-face Brep to merge.</param>
        /// <param name="tolerance">Surface edges must be within this tolerance for the two surfaces to merge.</param>
        /// <param name="angleToleranceRadians">Edge must be within this angle tolerance in order for contiguous edges to be combined into a single edge.</param>
        /// <returns>The merged Brep if successful, null if not successful.</returns>
        public static Brep MergeSurfaces(Brep brep0, Brep brep1, double tolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brep0, brep1, tolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Merges two surfaces into one surface at untrimmed edges. Both surfaces must be untrimmed and share an edge.
        /// </summary>
        /// <param name="brep0">The first single-face Brep to merge.</param>
        /// <param name="brep1">The second single-face Brep to merge.</param>
        /// <param name="tolerance">Surface edges must be within this tolerance for the two surfaces to merge.</param>
        /// <param name="angleToleranceRadians">Edge must be within this angle tolerance in order for contiguous edges to be combined into a single edge.</param>
        /// <returns>The merged Brep if successful, null if not successful.</returns>
        public static Brep MergeSurfaces(Remote<Brep> brep0, Remote<Brep> brep1, double tolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brep0, brep1, tolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Merges two surfaces into one surface at untrimmed edges. Both surfaces must be untrimmed and share an edge.
        /// </summary>
        /// <param name="brep0">The first single-face Brep to merge.</param>
        /// <param name="brep1">The second single-face Brep to merge.</param>
        /// <param name="tolerance">Surface edges must be within this tolerance for the two surfaces to merge.</param>
        /// <param name="angleToleranceRadians">Edge must be within this angle tolerance in order for contiguous edges to be combined into a single edge.</param>
        /// <param name="point0">2D pick point on the first single-face Brep. The value can be unset.</param>
        /// <param name="point1">2D pick point on the second single-face Brep. The value can be unset.</param>
        /// <param name="roundness">Defines the roundness of the merge. Acceptable values are between 0.0 (sharp) and 1.0 (smooth).</param>
        /// <param name="smooth">The surface will be smooth. This makes the surface behave better for control point editing, but may alter the shape of both surfaces.</param>
        /// <returns>The merged Brep if successful, null if not successful.</returns>
        public static Brep MergeSurfaces(Brep brep0, Brep brep1, double tolerance, double angleToleranceRadians, Point2d point0, Point2d point1, double roundness, bool smooth)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brep0, brep1, tolerance, angleToleranceRadians, point0, point1, roundness, smooth);
        }

        /// <summary>
        /// Merges two surfaces into one surface at untrimmed edges. Both surfaces must be untrimmed and share an edge.
        /// </summary>
        /// <param name="brep0">The first single-face Brep to merge.</param>
        /// <param name="brep1">The second single-face Brep to merge.</param>
        /// <param name="tolerance">Surface edges must be within this tolerance for the two surfaces to merge.</param>
        /// <param name="angleToleranceRadians">Edge must be within this angle tolerance in order for contiguous edges to be combined into a single edge.</param>
        /// <param name="point0">2D pick point on the first single-face Brep. The value can be unset.</param>
        /// <param name="point1">2D pick point on the second single-face Brep. The value can be unset.</param>
        /// <param name="roundness">Defines the roundness of the merge. Acceptable values are between 0.0 (sharp) and 1.0 (smooth).</param>
        /// <param name="smooth">The surface will be smooth. This makes the surface behave better for control point editing, but may alter the shape of both surfaces.</param>
        /// <returns>The merged Brep if successful, null if not successful.</returns>
        public static Brep MergeSurfaces(Remote<Brep> brep0, Remote<Brep> brep1, double tolerance, double angleToleranceRadians, Point2d point0, Point2d point1, double roundness, bool smooth)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brep0, brep1, tolerance, angleToleranceRadians, point0, point1, roundness, smooth);
        }

        /// <summary>
        /// Constructs a brep patch.
        /// <para>This is the simple version of fit that uses a specified starting surface.</para>
        /// </summary>
        /// <param name="geometry">
        /// Combination of Curves, BrepTrims, Points, PointClouds or Meshes.
        /// Curves and trims are sampled to get points. Trims are sampled for
        /// points and normals.
        /// </param>
        /// <param name="startingSurface">A starting surface (can be null).</param>
        /// <param name="tolerance">
        /// Tolerance used by input analysis functions for loop finding, trimming, etc.
        /// </param>
        /// <returns>
        /// Brep fit through input on success, or null on error.
        /// </returns>
        public static Brep CreatePatch(IEnumerable<GeometryBase> geometry, Surface startingSurface, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), geometry, startingSurface, tolerance);
        }

        /// <summary>
        /// Constructs a brep patch.
        /// <para>This is the simple version of fit that uses a specified starting surface.</para>
        /// </summary>
        /// <param name="geometry">
        /// Combination of Curves, BrepTrims, Points, PointClouds or Meshes.
        /// Curves and trims are sampled to get points. Trims are sampled for
        /// points and normals.
        /// </param>
        /// <param name="startingSurface">A starting surface (can be null).</param>
        /// <param name="tolerance">
        /// Tolerance used by input analysis functions for loop finding, trimming, etc.
        /// </param>
        /// <returns>
        /// Brep fit through input on success, or null on error.
        /// </returns>
        public static Brep CreatePatch(Remote<IEnumerable<GeometryBase>> geometry, Remote<Surface> startingSurface, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), geometry, startingSurface, tolerance);
        }

        /// <summary>
        /// Constructs a brep patch.
        /// <para>This is the simple version of fit that uses a plane with u x v spans.
        /// It makes a plane by fitting to the points from the input geometry to use as the starting surface.
        /// The surface has the specified u and v span count.</para>
        /// </summary>
        /// <param name="geometry">
        /// A combination of <see cref="Curve">curves</see>, brep trims,
        /// <see cref="Point">points</see>, <see cref="PointCloud">point clouds</see> or <see cref="Mesh">meshes</see>.
        /// Curves and trims are sampled to get points. Trims are sampled for
        /// points and normals.
        /// </param>
        /// <param name="uSpans">The number of spans in the U direction.</param>
        /// <param name="vSpans">The number of spans in the V direction.</param>
        /// <param name="tolerance">
        /// Tolerance used by input analysis functions for loop finding, trimming, etc.
        /// </param>
        /// <returns>
        /// A brep fit through input on success, or null on error.
        /// </returns>
        public static Brep CreatePatch(IEnumerable<GeometryBase> geometry, int uSpans, int vSpans, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), geometry, uSpans, vSpans, tolerance);
        }

        /// <summary>
        /// Constructs a brep patch.
        /// <para>This is the simple version of fit that uses a plane with u x v spans.
        /// It makes a plane by fitting to the points from the input geometry to use as the starting surface.
        /// The surface has the specified u and v span count.</para>
        /// </summary>
        /// <param name="geometry">
        /// A combination of <see cref="Curve">curves</see>, brep trims,
        /// <see cref="Point">points</see>, <see cref="PointCloud">point clouds</see> or <see cref="Mesh">meshes</see>.
        /// Curves and trims are sampled to get points. Trims are sampled for
        /// points and normals.
        /// </param>
        /// <param name="uSpans">The number of spans in the U direction.</param>
        /// <param name="vSpans">The number of spans in the V direction.</param>
        /// <param name="tolerance">
        /// Tolerance used by input analysis functions for loop finding, trimming, etc.
        /// </param>
        /// <returns>
        /// A brep fit through input on success, or null on error.
        /// </returns>
        public static Brep CreatePatch(Remote<IEnumerable<GeometryBase>> geometry, int uSpans, int vSpans, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), geometry, uSpans, vSpans, tolerance);
        }

        /// <summary>
        /// Constructs a brep patch using all controls
        /// </summary>
        /// <param name="geometry">
        /// A combination of <see cref="Curve">curves</see>, brep trims,
        /// <see cref="Point">points</see>, <see cref="PointCloud">point clouds</see> or <see cref="Mesh">meshes</see>.
        /// Curves and trims are sampled to get points. Trims are sampled for
        /// points and normals.
        /// </param>
        /// <param name="startingSurface">A starting surface (can be null).</param>
        /// <param name="uSpans">
        /// Number of surface spans used when a plane is fit through points to start in the U direction.
        /// </param>
        /// <param name="vSpans">
        /// Number of surface spans used when a plane is fit through points to start in the U direction.
        /// </param>
        /// <param name="trim">
        /// If true, try to find an outer loop from among the input curves and trim the result to that loop
        /// </param>
        /// <param name="tangency">
        /// If true, try to find brep trims in the outer loop of curves and try to
        /// fit to the normal direction of the trim's surface at those locations.
        /// </param>
        /// <param name="pointSpacing">
        /// Basic distance between points sampled from input curves.
        /// </param>
        /// <param name="flexibility">
        /// Determines the behavior of the surface in areas where its not otherwise
        /// controlled by the input.  Lower numbers make the surface behave more
        /// like a stiff material; higher, less like a stiff material.  That is,
        /// each span is made to more closely match the spans adjacent to it if there
        /// is no input geometry mapping to that area of the surface when the
        /// flexibility value is low.  The scale is logarithmic. Numbers around 0.001
        /// or 0.1 make the patch pretty stiff and numbers around 10 or 100 make the
        /// surface flexible.
        /// </param>
        /// <param name="surfacePull">
        /// Tends to keep the result surface where it was before the fit in areas where
        /// there is on influence from the input geometry
        /// </param>
        /// <param name="fixEdges">
        /// Array of four elements. Flags to keep the edges of a starting (untrimmed)
        /// surface in place while fitting the interior of the surface.  Order of
        /// flags is left, bottom, right, top
        ///</param>
        /// <param name="tolerance">
        /// Tolerance used by input analysis functions for loop finding, trimming, etc.
        /// </param>
        /// <returns>
        /// A brep fit through input on success, or null on error.
        /// </returns>
        public static Brep CreatePatch(IEnumerable<GeometryBase> geometry, Surface startingSurface, int uSpans, int vSpans, bool trim, bool tangency, double pointSpacing, double flexibility, double surfacePull, bool[] fixEdges, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), geometry, startingSurface, uSpans, vSpans, trim, tangency, pointSpacing, flexibility, surfacePull, fixEdges, tolerance);
        }

        /// <summary>
        /// Constructs a brep patch using all controls
        /// </summary>
        /// <param name="geometry">
        /// A combination of <see cref="Curve">curves</see>, brep trims,
        /// <see cref="Point">points</see>, <see cref="PointCloud">point clouds</see> or <see cref="Mesh">meshes</see>.
        /// Curves and trims are sampled to get points. Trims are sampled for
        /// points and normals.
        /// </param>
        /// <param name="startingSurface">A starting surface (can be null).</param>
        /// <param name="uSpans">
        /// Number of surface spans used when a plane is fit through points to start in the U direction.
        /// </param>
        /// <param name="vSpans">
        /// Number of surface spans used when a plane is fit through points to start in the U direction.
        /// </param>
        /// <param name="trim">
        /// If true, try to find an outer loop from among the input curves and trim the result to that loop
        /// </param>
        /// <param name="tangency">
        /// If true, try to find brep trims in the outer loop of curves and try to
        /// fit to the normal direction of the trim's surface at those locations.
        /// </param>
        /// <param name="pointSpacing">
        /// Basic distance between points sampled from input curves.
        /// </param>
        /// <param name="flexibility">
        /// Determines the behavior of the surface in areas where its not otherwise
        /// controlled by the input.  Lower numbers make the surface behave more
        /// like a stiff material; higher, less like a stiff material.  That is,
        /// each span is made to more closely match the spans adjacent to it if there
        /// is no input geometry mapping to that area of the surface when the
        /// flexibility value is low.  The scale is logarithmic. Numbers around 0.001
        /// or 0.1 make the patch pretty stiff and numbers around 10 or 100 make the
        /// surface flexible.
        /// </param>
        /// <param name="surfacePull">
        /// Tends to keep the result surface where it was before the fit in areas where
        /// there is on influence from the input geometry
        /// </param>
        /// <param name="fixEdges">
        /// Array of four elements. Flags to keep the edges of a starting (untrimmed)
        /// surface in place while fitting the interior of the surface.  Order of
        /// flags is left, bottom, right, top
        ///</param>
        /// <param name="tolerance">
        /// Tolerance used by input analysis functions for loop finding, trimming, etc.
        /// </param>
        /// <returns>
        /// A brep fit through input on success, or null on error.
        /// </returns>
        public static Brep CreatePatch(Remote<IEnumerable<GeometryBase>> geometry, Remote<Surface> startingSurface, int uSpans, int vSpans, bool trim, bool tangency, double pointSpacing, double flexibility, double surfacePull, bool[] fixEdges, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), geometry, startingSurface, uSpans, vSpans, trim, tangency, pointSpacing, flexibility, surfacePull, fixEdges, tolerance);
        }

        /// <summary>
        /// Creates a single walled pipe.
        /// </summary>
        /// <param name="rail">The rail, or path, curve.</param>
        /// <param name="radius">The radius of the pipe.</param>
        /// <param name="localBlending">
        /// The shape blending.
        /// If True, Local (pipe radius stays constant at the ends and changes more rapidly in the middle) is applied.
        /// If False, Global (radius is linearly blended from one end to the other, creating pipes that taper from one radius to the other) is applied.
        /// </param>
        /// <param name="cap">The end cap mode.</param>
        /// <param name="fitRail">
        /// If the curve is a polycurve of lines and arcs, the curve is fit and a single surface is created;
        /// otherwise the result is a Brep with joined surfaces created from the polycurve segments.
        /// </param>
        /// <param name="absoluteTolerance">
        /// The sweeping and fitting tolerance. When in doubt, use the document's absolute tolerance.
        /// </param>
        /// <param name="angleToleranceRadians">
        /// The angle tolerance. When in doubt, use the document's angle tolerance in radians.
        /// </param>
        /// <returns>Array of Breps success.</returns>
        public static Brep[] CreatePipe(Curve rail, double radius, bool localBlending, PipeCapMode cap, bool fitRail, double absoluteTolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, radius, localBlending, cap, fitRail, absoluteTolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Creates a single walled pipe.
        /// </summary>
        /// <param name="rail">The rail, or path, curve.</param>
        /// <param name="radius">The radius of the pipe.</param>
        /// <param name="localBlending">
        /// The shape blending.
        /// If True, Local (pipe radius stays constant at the ends and changes more rapidly in the middle) is applied.
        /// If False, Global (radius is linearly blended from one end to the other, creating pipes that taper from one radius to the other) is applied.
        /// </param>
        /// <param name="cap">The end cap mode.</param>
        /// <param name="fitRail">
        /// If the curve is a polycurve of lines and arcs, the curve is fit and a single surface is created;
        /// otherwise the result is a Brep with joined surfaces created from the polycurve segments.
        /// </param>
        /// <param name="absoluteTolerance">
        /// The sweeping and fitting tolerance. When in doubt, use the document's absolute tolerance.
        /// </param>
        /// <param name="angleToleranceRadians">
        /// The angle tolerance. When in doubt, use the document's angle tolerance in radians.
        /// </param>
        /// <returns>Array of Breps success.</returns>
        public static Brep[] CreatePipe(Remote<Curve> rail, double radius, bool localBlending, PipeCapMode cap, bool fitRail, double absoluteTolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, radius, localBlending, cap, fitRail, absoluteTolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Creates a single walled pipe.
        /// </summary>
        /// <param name="rail">The rail, or path, curve.</param>
        /// <param name="railRadiiParameters">
        /// One or more normalized curve parameters where changes in radius occur.
        /// Important: curve parameters must be normalized - ranging between 0.0 and 1.0.
        /// Use Interval.NormalizedParameterAt to calculate these.
        /// </param>
        /// <param name="radii">One or more radii - one at each normalized curve parameter in railRadiiParameters.</param>
        /// <param name="localBlending">
        /// The shape blending.
        /// If True, Local (pipe radius stays constant at the ends and changes more rapidly in the middle) is applied.
        /// If False, Global (radius is linearly blended from one end to the other, creating pipes that taper from one radius to the other) is applied.
        /// </param>
        /// <param name="cap">The end cap mode.</param>
        /// <param name="fitRail">
        /// If the curve is a polycurve of lines and arcs, the curve is fit and a single surface is created;
        /// otherwise the result is a Brep with joined surfaces created from the polycurve segments.
        /// </param>
        /// <param name="absoluteTolerance">
        /// The sweeping and fitting tolerance. When in doubt, use the document's absolute tolerance.
        /// </param>
        /// <param name="angleToleranceRadians">
        /// The angle tolerance. When in doubt, use the document's angle tolerance in radians.
        /// </param>
        /// <returns>Array of Breps success.</returns>
        public static Brep[] CreatePipe(Curve rail, IEnumerable<double> railRadiiParameters, IEnumerable<double> radii, bool localBlending, PipeCapMode cap, bool fitRail, double absoluteTolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, railRadiiParameters, radii, localBlending, cap, fitRail, absoluteTolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Creates a single walled pipe.
        /// </summary>
        /// <param name="rail">The rail, or path, curve.</param>
        /// <param name="railRadiiParameters">
        /// One or more normalized curve parameters where changes in radius occur.
        /// Important: curve parameters must be normalized - ranging between 0.0 and 1.0.
        /// Use Interval.NormalizedParameterAt to calculate these.
        /// </param>
        /// <param name="radii">One or more radii - one at each normalized curve parameter in railRadiiParameters.</param>
        /// <param name="localBlending">
        /// The shape blending.
        /// If True, Local (pipe radius stays constant at the ends and changes more rapidly in the middle) is applied.
        /// If False, Global (radius is linearly blended from one end to the other, creating pipes that taper from one radius to the other) is applied.
        /// </param>
        /// <param name="cap">The end cap mode.</param>
        /// <param name="fitRail">
        /// If the curve is a polycurve of lines and arcs, the curve is fit and a single surface is created;
        /// otherwise the result is a Brep with joined surfaces created from the polycurve segments.
        /// </param>
        /// <param name="absoluteTolerance">
        /// The sweeping and fitting tolerance. When in doubt, use the document's absolute tolerance.
        /// </param>
        /// <param name="angleToleranceRadians">
        /// The angle tolerance. When in doubt, use the document's angle tolerance in radians.
        /// </param>
        /// <returns>Array of Breps success.</returns>
        public static Brep[] CreatePipe(Remote<Curve> rail, IEnumerable<double> railRadiiParameters, IEnumerable<double> radii, bool localBlending, PipeCapMode cap, bool fitRail, double absoluteTolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, railRadiiParameters, radii, localBlending, cap, fitRail, absoluteTolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Creates a double-walled pipe.
        /// </summary>
        /// <param name="rail">The rail, or path, curve.</param>
        /// <param name="radius0">The first radius of the pipe.</param>
        /// <param name="radius1">The second radius of the pipe.</param>
        /// <param name="localBlending">
        /// The shape blending.
        /// If True, Local (pipe radius stays constant at the ends and changes more rapidly in the middle) is applied.
        /// If False, Global (radius is linearly blended from one end to the other, creating pipes that taper from one radius to the other) is applied.
        /// </param>
        /// <param name="cap">The end cap mode.</param>
        /// <param name="fitRail">
        /// If the curve is a polycurve of lines and arcs, the curve is fit and a single surface is created;
        /// otherwise the result is a Brep with joined surfaces created from the polycurve segments.
        /// </param>
        /// <param name="absoluteTolerance">
        /// The sweeping and fitting tolerance. When in doubt, use the document's absolute tolerance.
        /// </param>
        /// <param name="angleToleranceRadians">
        /// The angle tolerance. When in doubt, use the document's angle tolerance in radians.
        /// </param>
        /// <returns>Array of Breps success.</returns>
        public static Brep[] CreateThickPipe(Curve rail, double radius0, double radius1, bool localBlending, PipeCapMode cap, bool fitRail, double absoluteTolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, radius0, radius1, localBlending, cap, fitRail, absoluteTolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Creates a double-walled pipe.
        /// </summary>
        /// <param name="rail">The rail, or path, curve.</param>
        /// <param name="radius0">The first radius of the pipe.</param>
        /// <param name="radius1">The second radius of the pipe.</param>
        /// <param name="localBlending">
        /// The shape blending.
        /// If True, Local (pipe radius stays constant at the ends and changes more rapidly in the middle) is applied.
        /// If False, Global (radius is linearly blended from one end to the other, creating pipes that taper from one radius to the other) is applied.
        /// </param>
        /// <param name="cap">The end cap mode.</param>
        /// <param name="fitRail">
        /// If the curve is a polycurve of lines and arcs, the curve is fit and a single surface is created;
        /// otherwise the result is a Brep with joined surfaces created from the polycurve segments.
        /// </param>
        /// <param name="absoluteTolerance">
        /// The sweeping and fitting tolerance. When in doubt, use the document's absolute tolerance.
        /// </param>
        /// <param name="angleToleranceRadians">
        /// The angle tolerance. When in doubt, use the document's angle tolerance in radians.
        /// </param>
        /// <returns>Array of Breps success.</returns>
        public static Brep[] CreateThickPipe(Remote<Curve> rail, double radius0, double radius1, bool localBlending, PipeCapMode cap, bool fitRail, double absoluteTolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, radius0, radius1, localBlending, cap, fitRail, absoluteTolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Creates a double-walled pipe.
        /// </summary>
        /// <param name="rail">The rail, or path, curve.</param>
        /// <param name="railRadiiParameters">
        /// One or more normalized curve parameters where changes in radius occur.
        /// Important: curve parameters must be normalized - ranging between 0.0 and 1.0.
        /// Use Interval.NormalizedParameterAt to calculate these.
        /// </param>
        /// <param name="radii0">
        /// One or more radii for the first wall - one at each normalized curve parameter in railRadiiParameters.
        /// </param>
        /// <param name="radii1">
        /// One or more radii for the second wall - one at each normalized curve parameter in railRadiiParameters.
        /// </param>
        /// <param name="localBlending">
        /// The shape blending.
        /// If True, Local (pipe radius stays constant at the ends and changes more rapidly in the middle) is applied.
        /// If False, Global (radius is linearly blended from one end to the other, creating pipes that taper from one radius to the other) is applied.
        /// </param>
        /// <param name="cap">The end cap mode.</param>
        /// <param name="fitRail">
        /// If the curve is a polycurve of lines and arcs, the curve is fit and a single surface is created;
        /// otherwise the result is a Brep with joined surfaces created from the polycurve segments.
        /// </param>
        /// <param name="absoluteTolerance">
        /// The sweeping and fitting tolerance. When in doubt, use the document's absolute tolerance.
        /// </param>
        /// <param name="angleToleranceRadians">
        /// The angle tolerance. When in doubt, use the document's angle tolerance in radians.
        /// </param>
        /// <returns>Array of Breps success.</returns>
        public static Brep[] CreateThickPipe(Curve rail, IEnumerable<double> railRadiiParameters, IEnumerable<double> radii0, IEnumerable<double> radii1, bool localBlending, PipeCapMode cap, bool fitRail, double absoluteTolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, railRadiiParameters, radii0, radii1, localBlending, cap, fitRail, absoluteTolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Creates a double-walled pipe.
        /// </summary>
        /// <param name="rail">The rail, or path, curve.</param>
        /// <param name="railRadiiParameters">
        /// One or more normalized curve parameters where changes in radius occur.
        /// Important: curve parameters must be normalized - ranging between 0.0 and 1.0.
        /// Use Interval.NormalizedParameterAt to calculate these.
        /// </param>
        /// <param name="radii0">
        /// One or more radii for the first wall - one at each normalized curve parameter in railRadiiParameters.
        /// </param>
        /// <param name="radii1">
        /// One or more radii for the second wall - one at each normalized curve parameter in railRadiiParameters.
        /// </param>
        /// <param name="localBlending">
        /// The shape blending.
        /// If True, Local (pipe radius stays constant at the ends and changes more rapidly in the middle) is applied.
        /// If False, Global (radius is linearly blended from one end to the other, creating pipes that taper from one radius to the other) is applied.
        /// </param>
        /// <param name="cap">The end cap mode.</param>
        /// <param name="fitRail">
        /// If the curve is a polycurve of lines and arcs, the curve is fit and a single surface is created;
        /// otherwise the result is a Brep with joined surfaces created from the polycurve segments.
        /// </param>
        /// <param name="absoluteTolerance">
        /// The sweeping and fitting tolerance. When in doubt, use the document's absolute tolerance.
        /// </param>
        /// <param name="angleToleranceRadians">
        /// The angle tolerance. When in doubt, use the document's angle tolerance in radians.
        /// </param>
        /// <returns>Array of Breps success.</returns>
        public static Brep[] CreateThickPipe(Remote<Curve> rail, IEnumerable<double> railRadiiParameters, IEnumerable<double> radii0, IEnumerable<double> radii1, bool localBlending, PipeCapMode cap, bool fitRail, double absoluteTolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, railRadiiParameters, radii0, radii1, localBlending, cap, fitRail, absoluteTolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Sweep1 function that fits a surface through a profile curve that define the surface cross-sections
        /// and one curve that defines a surface edge. 
        /// </summary>
        /// <param name="rail">Rail to sweep shapes along</param>
        /// <param name="shape">Shape curve</param>
        /// <param name="closed">Only matters if shape is closed</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails</param>
        /// <returns>Array of Brep sweep results</returns>
        public static Brep[] CreateFromSweep(Curve rail, Curve shape, bool closed, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, shape, closed, tolerance);
        }

        /// <summary>
        /// Sweep1 function that fits a surface through a profile curve that define the surface cross-sections
        /// and one curve that defines a surface edge. 
        /// </summary>
        /// <param name="rail">Rail to sweep shapes along</param>
        /// <param name="shape">Shape curve</param>
        /// <param name="closed">Only matters if shape is closed</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails</param>
        /// <returns>Array of Brep sweep results</returns>
        public static Brep[] CreateFromSweep(Remote<Curve> rail, Remote<Curve> shape, bool closed, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, shape, closed, tolerance);
        }

        /// <summary>
        /// Sweep1 function that fits a surface through profile curves that define the surface cross-sections
        /// and one curve that defines a surface edge.
        /// </summary>
        /// <param name="rail">Rail to sweep shapes along</param>
        /// <param name="shapes">Shape curves</param>
        /// <param name="closed">Only matters if shapes are closed</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails</param>
        /// <returns>Array of Brep sweep results</returns>
        public static Brep[] CreateFromSweep(Curve rail, IEnumerable<Curve> shapes, bool closed, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, shapes, closed, tolerance);
        }

        /// <summary>
        /// Sweep1 function that fits a surface through profile curves that define the surface cross-sections
        /// and one curve that defines a surface edge.
        /// </summary>
        /// <param name="rail">Rail to sweep shapes along</param>
        /// <param name="shapes">Shape curves</param>
        /// <param name="closed">Only matters if shapes are closed</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails</param>
        /// <returns>Array of Brep sweep results</returns>
        public static Brep[] CreateFromSweep(Remote<Curve> rail, Remote<IEnumerable<Curve>> shapes, bool closed, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, shapes, closed, tolerance);
        }

        /// <summary>
        /// Sweep1 function that fits a surface through a series of profile curves that define the surface cross-sections
        /// and one curve that defines a surface edge.
        /// </summary>
        /// <param name="rail">Rail to sweep shapes along.</param>
        /// <param name="shapes">Shape curves.</param>
        /// <param name="startPoint">Optional starting point of sweep. Use Point3d.Unset if you do not want to include a start point.</param>
        /// <param name="endPoint">Optional ending point of sweep. Use Point3d.Unset if you do not want to include an end point.</param>
        /// <param name="frameType">The frame type.</param>
        /// <param name="roadlikeNormal">The roadlike normal directoion. Use Vector3d.Unset if the frame type is not set to roadlike.</param>
        /// <param name="closed">Only matters if shapes are closed.</param>
        /// <param name="blendType">The shape blending type.</param>
        /// <param name="miterType">The mitering type.</param>
        /// <param name="tolerance"></param>
        /// <param name="rebuildType">The rebuild style.</param>
        /// <param name="rebuildPointCount">If rebuild == SweepRebuild.Rebuild, the number of points. Otherwise specify 0.</param>
        /// <param name="refitTolerance">If rebuild == SweepRebuild.Refit, the refit tolerance. Otherwise, specify 0.0</param>
        /// <returns>Array of Brep sweep results.</returns>
        public static Brep[] CreateFromSweep(Curve rail, IEnumerable<Curve> shapes, Point3d startPoint, Point3d endPoint, SweepFrame frameType, Vector3d roadlikeNormal, bool closed, SweepBlend blendType, SweepMiter miterType, double tolerance, SweepRebuild rebuildType, int rebuildPointCount, double refitTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, shapes, startPoint, endPoint, frameType, roadlikeNormal, closed, blendType, miterType, tolerance, rebuildType, rebuildPointCount, refitTolerance);
        }

        /// <summary>
        /// Sweep1 function that fits a surface through a series of profile curves that define the surface cross-sections
        /// and one curve that defines a surface edge.
        /// </summary>
        /// <param name="rail">Rail to sweep shapes along.</param>
        /// <param name="shapes">Shape curves.</param>
        /// <param name="startPoint">Optional starting point of sweep. Use Point3d.Unset if you do not want to include a start point.</param>
        /// <param name="endPoint">Optional ending point of sweep. Use Point3d.Unset if you do not want to include an end point.</param>
        /// <param name="frameType">The frame type.</param>
        /// <param name="roadlikeNormal">The roadlike normal directoion. Use Vector3d.Unset if the frame type is not set to roadlike.</param>
        /// <param name="closed">Only matters if shapes are closed.</param>
        /// <param name="blendType">The shape blending type.</param>
        /// <param name="miterType">The mitering type.</param>
        /// <param name="tolerance"></param>
        /// <param name="rebuildType">The rebuild style.</param>
        /// <param name="rebuildPointCount">If rebuild == SweepRebuild.Rebuild, the number of points. Otherwise specify 0.</param>
        /// <param name="refitTolerance">If rebuild == SweepRebuild.Refit, the refit tolerance. Otherwise, specify 0.0</param>
        /// <returns>Array of Brep sweep results.</returns>
        public static Brep[] CreateFromSweep(Remote<Curve> rail, Remote<IEnumerable<Curve>> shapes, Point3d startPoint, Point3d endPoint, SweepFrame frameType, Vector3d roadlikeNormal, bool closed, SweepBlend blendType, SweepMiter miterType, double tolerance, SweepRebuild rebuildType, int rebuildPointCount, double refitTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, shapes, startPoint, endPoint, frameType, roadlikeNormal, closed, blendType, miterType, tolerance, rebuildType, rebuildPointCount, refitTolerance);
        }

        /// <summary>
        /// Sweep1 function that fits a surface through a profile curve that define the surface cross-sections
        /// and one curve that defines a surface edge. The Segmented version breaks the rail at curvature kinks
        /// and sweeps each piece separately, then put the results together into a Brep.
        /// </summary>
        /// <param name="rail">Rail to sweep shapes along</param>
        /// <param name="shape">Shape curve</param>
        /// <param name="closed">Only matters if shape is closed</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails</param>
        /// <returns>Array of Brep sweep results</returns>
        public static Brep[] CreateFromSweepSegmented(Curve rail, Curve shape, bool closed, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, shape, closed, tolerance);
        }

        /// <summary>
        /// Sweep1 function that fits a surface through a profile curve that define the surface cross-sections
        /// and one curve that defines a surface edge. The Segmented version breaks the rail at curvature kinks
        /// and sweeps each piece separately, then put the results together into a Brep.
        /// </summary>
        /// <param name="rail">Rail to sweep shapes along</param>
        /// <param name="shape">Shape curve</param>
        /// <param name="closed">Only matters if shape is closed</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails</param>
        /// <returns>Array of Brep sweep results</returns>
        public static Brep[] CreateFromSweepSegmented(Remote<Curve> rail, Remote<Curve> shape, bool closed, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, shape, closed, tolerance);
        }

        /// <summary>
        /// Sweep1 function that fits a surface through a series of profile curves that define the surface cross-sections
        /// and one curve that defines a surface edge. The Segmented version breaks the rail at curvature kinks
        /// and sweeps each piece separately, then put the results together into a Brep.
        /// </summary>
        /// <param name="rail">Rail to sweep shapes along.</param>
        /// <param name="shapes">Shape curves.</param>
        /// <param name="closed">Only matters if shapes are closed.</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails.</param>
        /// <returns>Array of Brep sweep results.</returns>
        public static Brep[] CreateFromSweepSegmented(Curve rail, IEnumerable<Curve> shapes, bool closed, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, shapes, closed, tolerance);
        }

        /// <summary>
        /// Sweep1 function that fits a surface through a series of profile curves that define the surface cross-sections
        /// and one curve that defines a surface edge. The Segmented version breaks the rail at curvature kinks
        /// and sweeps each piece separately, then put the results together into a Brep.
        /// </summary>
        /// <param name="rail">Rail to sweep shapes along.</param>
        /// <param name="shapes">Shape curves.</param>
        /// <param name="closed">Only matters if shapes are closed.</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails.</param>
        /// <returns>Array of Brep sweep results.</returns>
        public static Brep[] CreateFromSweepSegmented(Remote<Curve> rail, Remote<IEnumerable<Curve>> shapes, bool closed, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, shapes, closed, tolerance);
        }

        /// <summary>
        /// Sweep1 function that fits a surface through a series of profile curves that define the surface cross-sections
        /// and one curve that defines a surface edge. The Segmented version breaks the rail at curvature kinks
        /// and sweeps each piece separately, then put the results together into a Brep.
        /// </summary>
        /// <param name="rail">Rail to sweep shapes along.</param>
        /// <param name="shapes">Shape curves.</param>
        /// <param name="startPoint">Optional starting point of sweep. Use Point3d.Unset if you do not want to include a start point.</param>
        /// <param name="endPoint">Optional ending point of sweep. Use Point3d.Unset if you do not want to include an end point.</param>
        /// <param name="frameType">The frame type.</param>
        /// <param name="roadlikeNormal">The roadlike normal directoion. Use Vector3d.Unset if the frame type is not set to roadlike.</param>
        /// <param name="closed">Only matters if shapes are closed.</param>
        /// <param name="blendType">The shape blending type.</param>
        /// <param name="miterType">The mitering type.</param>
        /// <param name="tolerance"></param>
        /// <param name="rebuildType">The rebuild style.</param>
        /// <param name="rebuildPointCount">If rebuild == SweepRebuild.Rebuild, the number of points. Otherwise specify 0.</param>
        /// <param name="refitTolerance">If rebuild == SweepRebuild.Refit, the refit tolerance. Otherwise, specify 0.0</param>
        /// <returns>Array of Brep sweep results.</returns>
        public static Brep[] CreateFromSweepSegmented(Curve rail, IEnumerable<Curve> shapes, Point3d startPoint, Point3d endPoint, SweepFrame frameType, Vector3d roadlikeNormal, bool closed, SweepBlend blendType, SweepMiter miterType, double tolerance, SweepRebuild rebuildType, int rebuildPointCount, double refitTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, shapes, startPoint, endPoint, frameType, roadlikeNormal, closed, blendType, miterType, tolerance, rebuildType, rebuildPointCount, refitTolerance);
        }

        /// <summary>
        /// Sweep1 function that fits a surface through a series of profile curves that define the surface cross-sections
        /// and one curve that defines a surface edge. The Segmented version breaks the rail at curvature kinks
        /// and sweeps each piece separately, then put the results together into a Brep.
        /// </summary>
        /// <param name="rail">Rail to sweep shapes along.</param>
        /// <param name="shapes">Shape curves.</param>
        /// <param name="startPoint">Optional starting point of sweep. Use Point3d.Unset if you do not want to include a start point.</param>
        /// <param name="endPoint">Optional ending point of sweep. Use Point3d.Unset if you do not want to include an end point.</param>
        /// <param name="frameType">The frame type.</param>
        /// <param name="roadlikeNormal">The roadlike normal directoion. Use Vector3d.Unset if the frame type is not set to roadlike.</param>
        /// <param name="closed">Only matters if shapes are closed.</param>
        /// <param name="blendType">The shape blending type.</param>
        /// <param name="miterType">The mitering type.</param>
        /// <param name="tolerance"></param>
        /// <param name="rebuildType">The rebuild style.</param>
        /// <param name="rebuildPointCount">If rebuild == SweepRebuild.Rebuild, the number of points. Otherwise specify 0.</param>
        /// <param name="refitTolerance">If rebuild == SweepRebuild.Refit, the refit tolerance. Otherwise, specify 0.0</param>
        /// <returns>Array of Brep sweep results.</returns>
        public static Brep[] CreateFromSweepSegmented(Remote<Curve> rail, Remote<IEnumerable<Curve>> shapes, Point3d startPoint, Point3d endPoint, SweepFrame frameType, Vector3d roadlikeNormal, bool closed, SweepBlend blendType, SweepMiter miterType, double tolerance, SweepRebuild rebuildType, int rebuildPointCount, double refitTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail, shapes, startPoint, endPoint, frameType, roadlikeNormal, closed, blendType, miterType, tolerance, rebuildType, rebuildPointCount, refitTolerance);
        }

        /// <summary>
        /// General 2 rail sweep. If you are not producing the sweep results that you are after, then
        /// use the SweepTwoRail class with options to generate the swept geometry.
        /// </summary>
        /// <param name="rail1">Rail to sweep shapes along</param>
        /// <param name="rail2">Rail to sweep shapes along</param>
        /// <param name="shape">Shape curve</param>
        /// <param name="closed">Only matters if shape is closed</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails</param>
        /// <returns>Array of Brep sweep results</returns>
        public static Brep[] CreateFromSweep(Curve rail1, Curve rail2, Curve shape, bool closed, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail1, rail2, shape, closed, tolerance);
        }

        /// <summary>
        /// General 2 rail sweep. If you are not producing the sweep results that you are after, then
        /// use the SweepTwoRail class with options to generate the swept geometry.
        /// </summary>
        /// <param name="rail1">Rail to sweep shapes along</param>
        /// <param name="rail2">Rail to sweep shapes along</param>
        /// <param name="shape">Shape curve</param>
        /// <param name="closed">Only matters if shape is closed</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails</param>
        /// <returns>Array of Brep sweep results</returns>
        public static Brep[] CreateFromSweep(Remote<Curve> rail1, Remote<Curve> rail2, Remote<Curve> shape, bool closed, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail1, rail2, shape, closed, tolerance);
        }

        /// <summary>
        /// General 2 rail sweep. If you are not producing the sweep results that you are after, then
        /// use the SweepTwoRail class with options to generate the swept geometry.
        /// </summary>
        /// <param name="rail1">Rail to sweep shapes along</param>
        /// <param name="rail2">Rail to sweep shapes along</param>
        /// <param name="shapes">Shape curves</param>
        /// <param name="closed">Only matters if shapes are closed</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails</param>
        /// <returns>Array of Brep sweep results</returns>
        public static Brep[] CreateFromSweep(Curve rail1, Curve rail2, IEnumerable<Curve> shapes, bool closed, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail1, rail2, shapes, closed, tolerance);
        }

        /// <summary>
        /// General 2 rail sweep. If you are not producing the sweep results that you are after, then
        /// use the SweepTwoRail class with options to generate the swept geometry.
        /// </summary>
        /// <param name="rail1">Rail to sweep shapes along</param>
        /// <param name="rail2">Rail to sweep shapes along</param>
        /// <param name="shapes">Shape curves</param>
        /// <param name="closed">Only matters if shapes are closed</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails</param>
        /// <returns>Array of Brep sweep results</returns>
        public static Brep[] CreateFromSweep(Remote<Curve> rail1, Remote<Curve> rail2, Remote<IEnumerable<Curve>> shapes, bool closed, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail1, rail2, shapes, closed, tolerance);
        }

        /// <summary>
        /// Sweep2 function that fits a surface through profile curves that define the surface cross-sections
        /// and two curves that defines the surface edges.
        /// </summary>
        /// <param name="rail1">Rail to sweep shapes along</param>
        /// <param name="rail2">Rail to sweep shapes along</param>
        /// <param name="shapes">Shape curves</param>
        /// <param name="start">Optional starting point of sweep. Use Point3d.Unset if you do not want to include a start point.</param>
        /// <param name="end">Optional ending point of sweep. Use Point3d.Unset if you do not want to include an end point.</param>
        /// <param name="closed">Only matters if shapes are closed.</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails.</param>
        /// <param name="rebuild">The rebuild style.</param>
        /// <param name="rebuildPointCount">If rebuild == SweepRebuild.Rebuild, the number of points. Otherwise specify 0.</param>
        /// <param name="refitTolerance">If rebuild == SweepRebuild.Refit, the refit tolerance. Otherwise, specify 0.0</param>
        /// <param name="preserveHeight">Removes the association between the height scaling from the width scaling</param>
        /// <returns>Array of Brep sweep results</returns>
        public static Brep[] CreateFromSweep(Curve rail1, Curve rail2, IEnumerable<Curve> shapes, Point3d start, Point3d end, bool closed, double tolerance, SweepRebuild rebuild, int rebuildPointCount, double refitTolerance, bool preserveHeight)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail1, rail2, shapes, start, end, closed, tolerance, rebuild, rebuildPointCount, refitTolerance, preserveHeight);
        }

        /// <summary>
        /// Sweep2 function that fits a surface through profile curves that define the surface cross-sections
        /// and two curves that defines the surface edges.
        /// </summary>
        /// <param name="rail1">Rail to sweep shapes along</param>
        /// <param name="rail2">Rail to sweep shapes along</param>
        /// <param name="shapes">Shape curves</param>
        /// <param name="start">Optional starting point of sweep. Use Point3d.Unset if you do not want to include a start point.</param>
        /// <param name="end">Optional ending point of sweep. Use Point3d.Unset if you do not want to include an end point.</param>
        /// <param name="closed">Only matters if shapes are closed.</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails.</param>
        /// <param name="rebuild">The rebuild style.</param>
        /// <param name="rebuildPointCount">If rebuild == SweepRebuild.Rebuild, the number of points. Otherwise specify 0.</param>
        /// <param name="refitTolerance">If rebuild == SweepRebuild.Refit, the refit tolerance. Otherwise, specify 0.0</param>
        /// <param name="preserveHeight">Removes the association between the height scaling from the width scaling</param>
        /// <returns>Array of Brep sweep results</returns>
        public static Brep[] CreateFromSweep(Remote<Curve> rail1, Remote<Curve> rail2, Remote<IEnumerable<Curve>> shapes, Point3d start, Point3d end, bool closed, double tolerance, SweepRebuild rebuild, int rebuildPointCount, double refitTolerance, bool preserveHeight)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail1, rail2, shapes, start, end, closed, tolerance, rebuild, rebuildPointCount, refitTolerance, preserveHeight);
        }

        /// <summary>
        /// Makes a 2 rail sweep. Like CreateFromSweep but the result is split where parameterization along a rail changes abruptly.
        /// </summary>
        /// <param name="rail1">Rail to sweep shapes along</param>
        /// <param name="rail2">Rail to sweep shapes along</param>
        /// <param name="shapes">Shape curves</param>        /// <param name="rail_params">Shape parameters</param>
        /// <param name="closed">Only matters if shapes are closed</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails</param>
        /// <returns>Array of Brep sweep results</returns>
        public static Brep[] CreateFromSweepInParts(Curve rail1, Curve rail2, IEnumerable<Curve> shapes, IEnumerable<Point2d> rail_params, bool closed, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail1, rail2, shapes, rail_params, closed, tolerance);
        }

        /// <summary>
        /// Makes a 2 rail sweep. Like CreateFromSweep but the result is split where parameterization along a rail changes abruptly.
        /// </summary>
        /// <param name="rail1">Rail to sweep shapes along</param>
        /// <param name="rail2">Rail to sweep shapes along</param>
        /// <param name="shapes">Shape curves</param>        /// <param name="rail_params">Shape parameters</param>
        /// <param name="closed">Only matters if shapes are closed</param>
        /// <param name="tolerance">Tolerance for fitting surface and rails</param>
        /// <returns>Array of Brep sweep results</returns>
        public static Brep[] CreateFromSweepInParts(Remote<Curve> rail1, Remote<Curve> rail2, Remote<IEnumerable<Curve>> shapes, IEnumerable<Point2d> rail_params, bool closed, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), rail1, rail2, shapes, rail_params, closed, tolerance);
        }

        /// <summary>
        /// Extrude a curve to a taper making a brep (potentially more than 1)
        /// </summary>
        /// <param name="curveToExtrude">the curve to extrude</param>
        /// <param name="distance">the distance to extrude</param>
        /// <param name="direction">the direction of the extrusion</param>
        /// <param name="basePoint">the base point of the extrusion</param>
        /// <param name="draftAngleRadians">angle of the extrusion</param>
        /// <param name="cornerType"></param>
        /// <param name="tolerance">tolerance to use for the extrusion</param>
        /// <param name="angleToleranceRadians">angle tolerance to use for the extrusion</param>
        /// <returns>array of breps on success</returns>
        public static Brep[] CreateFromTaperedExtrude(Curve curveToExtrude, double distance, Vector3d direction, Point3d basePoint, double draftAngleRadians, ExtrudeCornerType cornerType, double tolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), curveToExtrude, distance, direction, basePoint, draftAngleRadians, cornerType, tolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Extrude a curve to a taper making a brep (potentially more than 1)
        /// </summary>
        /// <param name="curveToExtrude">the curve to extrude</param>
        /// <param name="distance">the distance to extrude</param>
        /// <param name="direction">the direction of the extrusion</param>
        /// <param name="basePoint">the base point of the extrusion</param>
        /// <param name="draftAngleRadians">angle of the extrusion</param>
        /// <param name="cornerType"></param>
        /// <param name="tolerance">tolerance to use for the extrusion</param>
        /// <param name="angleToleranceRadians">angle tolerance to use for the extrusion</param>
        /// <returns>array of breps on success</returns>
        public static Brep[] CreateFromTaperedExtrude(Remote<Curve> curveToExtrude, double distance, Vector3d direction, Point3d basePoint, double draftAngleRadians, ExtrudeCornerType cornerType, double tolerance, double angleToleranceRadians)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), curveToExtrude, distance, direction, basePoint, draftAngleRadians, cornerType, tolerance, angleToleranceRadians);
        }

        /// <summary>
        /// Extrude a curve to a taper making a brep (potentially more than 1)
        /// </summary>
        /// <param name="curveToExtrude">the curve to extrude</param>
        /// <param name="distance">the distance to extrude</param>
        /// <param name="direction">the direction of the extrusion</param>
        /// <param name="basePoint">the base point of the extrusion</param>
        /// <param name="draftAngleRadians">angle of the extrusion</param>
        /// <param name="cornerType"></param>
        /// <returns>array of breps on success</returns>
        /// <remarks>tolerances used are based on the active doc tolerance</remarks>
        /// <deprecated>6.0</deprecated>
        public static Brep[] CreateFromTaperedExtrude(Curve curveToExtrude, double distance, Vector3d direction, Point3d basePoint, double draftAngleRadians, ExtrudeCornerType cornerType)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), curveToExtrude, distance, direction, basePoint, draftAngleRadians, cornerType);
        }

        /// <summary>
        /// Extrude a curve to a taper making a brep (potentially more than 1)
        /// </summary>
        /// <param name="curveToExtrude">the curve to extrude</param>
        /// <param name="distance">the distance to extrude</param>
        /// <param name="direction">the direction of the extrusion</param>
        /// <param name="basePoint">the base point of the extrusion</param>
        /// <param name="draftAngleRadians">angle of the extrusion</param>
        /// <param name="cornerType"></param>
        /// <returns>array of breps on success</returns>
        /// <remarks>tolerances used are based on the active doc tolerance</remarks>
        /// <deprecated>6.0</deprecated>
        public static Brep[] CreateFromTaperedExtrude(Remote<Curve> curveToExtrude, double distance, Vector3d direction, Point3d basePoint, double draftAngleRadians, ExtrudeCornerType cornerType)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), curveToExtrude, distance, direction, basePoint, draftAngleRadians, cornerType);
        }

        /// <summary>
        /// Creates one or more Breps by extruding a curve a distance along an axis with draft angle.
        /// </summary>
        /// <param name="curve">The curve to extrude.</param>
        /// <param name="direction">The extrusion direction.</param>
        /// <param name="distance">The extrusion distance.</param>
        /// <param name="draftAngle">The extrusion draft angle in radians.</param>
        /// <param name="plane">
        /// The end of the extrusion will be parallel to this plane, and "distance" from the plane's origin.
        /// The plane's origin is generally be a point on the curve. For planar curves, a natural choice for the
        /// plane's normal direction will be the normal direction of the curve's plane. In any case, 
        /// plane.Normal = direction may make sense.
        /// </param>
        /// <param name="tolerance">The intersecting and trimming tolerance.</param>
        /// <returns>An array of Breps if successful.</returns>
        public static Brep[] CreateFromTaperedExtrudeWithRef(Curve curve, Vector3d direction, double distance, double draftAngle, Plane plane, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), curve, direction, distance, draftAngle, plane, tolerance);
        }

        /// <summary>
        /// Creates one or more Breps by extruding a curve a distance along an axis with draft angle.
        /// </summary>
        /// <param name="curve">The curve to extrude.</param>
        /// <param name="direction">The extrusion direction.</param>
        /// <param name="distance">The extrusion distance.</param>
        /// <param name="draftAngle">The extrusion draft angle in radians.</param>
        /// <param name="plane">
        /// The end of the extrusion will be parallel to this plane, and "distance" from the plane's origin.
        /// The plane's origin is generally be a point on the curve. For planar curves, a natural choice for the
        /// plane's normal direction will be the normal direction of the curve's plane. In any case, 
        /// plane.Normal = direction may make sense.
        /// </param>
        /// <param name="tolerance">The intersecting and trimming tolerance.</param>
        /// <returns>An array of Breps if successful.</returns>
        public static Brep[] CreateFromTaperedExtrudeWithRef(Remote<Curve> curve, Vector3d direction, double distance, double draftAngle, Plane plane, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), curve, direction, distance, draftAngle, plane, tolerance);
        }

        /// <summary>
        /// Makes a surface blend between two surface edges.
        /// </summary>
        /// <param name="face0">First face to blend from.</param>
        /// <param name="edge0">First edge to blend from.</param>
        /// <param name="domain0">The domain of edge0 to use.</param>
        /// <param name="rev0">If false, edge0 will be used in its natural direction. If true, edge0 will be used in the reversed direction.</param>
        /// <param name="continuity0">Continuity for the blend at the start.</param>
        /// <param name="face1">Second face to blend from.</param>
        /// <param name="edge1">Second edge to blend from.</param>
        /// <param name="domain1">The domain of edge1 to use.</param>
        /// <param name="rev1">If false, edge1 will be used in its natural direction. If true, edge1 will be used in the reversed direction.</param>
        /// <param name="continuity1">Continuity for the blend at the end.</param>
        /// <returns>Array of Breps if successful.</returns>
        public static Brep[] CreateBlendSurface(BrepFace face0, BrepEdge edge0, Interval domain0, bool rev0, BlendContinuity continuity0, BrepFace face1, BrepEdge edge1, Interval domain1, bool rev1, BlendContinuity continuity1)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), face0, edge0, domain0, rev0, continuity0, face1, edge1, domain1, rev1, continuity1);
        }

        /// <summary>
        /// Makes a curve blend between points on two surface edges. The blend will be tangent to the surfaces and perpendicular to the edges.
        /// </summary>
        /// <param name="face0">First face to blend from.</param>
        /// <param name="edge0">First edge to blend from.</param>
        /// <param name="t0">Location on first edge for first end of blend curve.</param>
        /// <param name="rev0">If false, edge0 will be used in its natural direction. If true, edge0 will be used in the reversed direction.</param>
        /// <param name="continuity0">Continuity for the blend at the start.</param>
        /// <param name="face1">Second face to blend from.</param>
        /// <param name="edge1">Second edge to blend from.</param>
        /// <param name="t1">Location on second edge for second end of blend curve.</param>
        /// <param name="rev1">If false, edge1 will be used in its natural direction. If true, edge1 will be used in the reversed direction.</param>
        /// <param name="continuity1">>Continuity for the blend at the end.</param>
        /// <returns>The blend curve on success. null on failure</returns>
        public static Curve CreateBlendShape(BrepFace face0, BrepEdge edge0, double t0, bool rev0, BlendContinuity continuity0, BrepFace face1, BrepEdge edge1, double t1, bool rev1, BlendContinuity continuity1)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), face0, edge0, t0, rev0, continuity0, face1, edge1, t1, rev1, continuity1);
        }

        /// <summary>
        /// Creates a constant-radius round surface between two surfaces.
        /// </summary>
        /// <param name="face0">First face to fillet from.</param>
        /// <param name="uv0">A parameter face0 at the side you want to keep after filleting.</param>
        /// <param name="face1">Second face to fillet from.</param>
        /// <param name="uv1">A parameter face1 at the side you want to keep after filleting.</param>
        /// <param name="radius">The fillet radius.</param>
        /// <param name="extend">If true, then when one input surface is longer than the other, the fillet surface is extended to the input surface edges.</param>
        /// <param name="tolerance">The tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>Array of Breps if successful.</returns>
        public static Brep[] CreateFilletSurface(BrepFace face0, Point2d uv0, BrepFace face1, Point2d uv1, double radius, bool extend, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), face0, uv0, face1, uv1, radius, extend, tolerance);
        }

        /// <summary>
        ///  Creates a constant-radius round surface between two surfaces.
        /// </summary>
        /// <param name="face0">First face to fillet from.</param>
        /// <param name="uv0">A parameter face0 at the side you want to keep after filleting.</param>
        /// <param name="face1">Second face to fillet from.</param>
        /// <param name="uv1">A parameter face1 at the side you want to keep after filleting.</param>
        /// <param name="radius">The fillet radius.</param>
        /// <param name="trim">If true, the input faces will be trimmed, if false, the input faces will be split.</param>
        /// <param name="extend">If true, then when one input surface is longer than the other, the fillet surface is extended to the input surface edges.</param>
        /// <param name="tolerance">The tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <param name="outBreps0">The trim or split results of the Brep owned by face0.</param>
        /// <param name="outBreps1">The trim or split results of the Brep owned by face1.</param>
        /// <returns>Array of Breps if successful.</returns>
        public static Brep[] CreateFilletSurface(BrepFace face0, Point2d uv0, BrepFace face1, Point2d uv1, double radius, bool trim, bool extend, double tolerance, out Brep[] outBreps0, out Brep[] outBreps1)
        {
            return ComputeServer.Post<Brep[], Brep[], Brep[]>(ApiAddress(), out outBreps0, out outBreps1, face0, uv0, face1, uv1, radius, trim, extend, tolerance);
        }

        /// <summary>
        /// Creates a ruled surface as a bevel between two input surface edges.
        /// </summary>
        /// <param name="face0">First face to chamfer from.</param>
        /// <param name="uv0">A parameter face0 at the side you want to keep after chamfering.</param>
        /// <param name="radius0">The distance from the intersection of face0 to the edge of the chamfer.</param>
        /// <param name="face1">Second face to chamfer from.</param>
        /// <param name="uv1">A parameter face1 at the side you want to keep after chamfering.</param>
        /// <param name="radius1">The distance from the intersection of face1 to the edge of the chamfer.</param>
        /// <param name="extend">If true, then when one input surface is longer than the other, the chamfer surface is extended to the input surface edges.</param>
        /// <param name="tolerance">The tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>Array of Breps if successful.</returns>
        public static Brep[] CreateChamferSurface(BrepFace face0, Point2d uv0, double radius0, BrepFace face1, Point2d uv1, double radius1, bool extend, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), face0, uv0, radius0, face1, uv1, radius1, extend, tolerance);
        }

        /// <summary>
        /// Creates a ruled surface as a bevel between two input surface edges.
        /// </summary>
        /// <param name="face0">First face to chamfer from.</param>
        /// <param name="uv0">A parameter face0 at the side you want to keep after chamfering.</param>
        /// <param name="radius0">The distance from the intersection of face0 to the edge of the chamfer.</param>
        /// <param name="face1">Second face to chamfer from.</param>
        /// <param name="uv1">A parameter face1 at the side you want to keep after chamfering.</param>
        /// <param name="radius1">The distance from the intersection of face1 to the edge of the chamfer.</param>
        /// <param name="trim">If true, the input faces will be trimmed, if false, the input faces will be split.</param>
        /// <param name="extend">If true, then when one input surface is longer than the other, the chamfer surface is extended to the input surface edges.</param>
        /// <param name="tolerance">The tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <param name="outBreps0">The trim or split results of the Brep owned by face0.</param>
        /// <param name="outBreps1">The trim or split results of the Brep owned by face1.</param>
        /// <returns>Array of Breps if successful.</returns>
        public static Brep[] CreateChamferSurface(BrepFace face0, Point2d uv0, double radius0, BrepFace face1, Point2d uv1, double radius1, bool trim, bool extend, double tolerance, out Brep[] outBreps0, out Brep[] outBreps1)
        {
            return ComputeServer.Post<Brep[], Brep[], Brep[]>(ApiAddress(), out outBreps0, out outBreps1, face0, uv0, radius0, face1, uv1, radius1, trim, extend, tolerance);
        }

        /// <summary>
        /// Fillets, chamfers, or blends the edges of a brep.
        /// </summary>
        /// <param name="brep">The brep to fillet, chamfer, or blend edges.</param>
        /// <param name="edgeIndices">An array of one or more edge indices where the fillet, chamfer, or blend will occur.</param>
        /// <param name="startRadii">An array of starting fillet, chamfer, or blend radaii, one for each edge index.</param>
        /// <param name="endRadii">An array of ending fillet, chamfer, or blend radaii, one for each edge index.</param>
        /// <param name="blendType">The blend type.</param>
        /// <param name="railType">The rail type.</param>
        /// <param name="tolerance">The tolerance to be used to perform calculations.</param>
        /// <returns>Array of Breps if successful.</returns>
        public static Brep[] CreateFilletEdges(Brep brep, IEnumerable<int> edgeIndices, IEnumerable<double> startRadii, IEnumerable<double> endRadii, BlendType blendType, RailType railType, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, edgeIndices, startRadii, endRadii, blendType, railType, tolerance);
        }

        /// <summary>
        /// Fillets, chamfers, or blends the edges of a brep.
        /// </summary>
        /// <param name="brep">The brep to fillet, chamfer, or blend edges.</param>
        /// <param name="edgeIndices">An array of one or more edge indices where the fillet, chamfer, or blend will occur.</param>
        /// <param name="startRadii">An array of starting fillet, chamfer, or blend radaii, one for each edge index.</param>
        /// <param name="endRadii">An array of ending fillet, chamfer, or blend radaii, one for each edge index.</param>
        /// <param name="blendType">The blend type.</param>
        /// <param name="railType">The rail type.</param>
        /// <param name="tolerance">The tolerance to be used to perform calculations.</param>
        /// <returns>Array of Breps if successful.</returns>
        public static Brep[] CreateFilletEdges(Remote<Brep> brep, IEnumerable<int> edgeIndices, IEnumerable<double> startRadii, IEnumerable<double> endRadii, BlendType blendType, RailType railType, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, edgeIndices, startRadii, endRadii, blendType, railType, tolerance);
        }

        /// <summary>
        /// Offsets a Brep.
        /// </summary>
        /// <param name="brep">The Brep to offset.</param>
        /// <param name="distance">
        /// The distance to offset. This is a signed distance value with respect to
        /// face normals and flipped faces.
        /// </param>
        /// <param name="solid">
        /// If true, then the function makes a closed solid from the input and offset
        /// surfaces by lofting a ruled surface between all of the matching edges.
        /// </param>
        /// <param name="extend">
        /// If true, then the function maintains the sharp corners when the original
        /// surfaces have sharps corner. If False, then the function creates fillets
        /// at sharp corners in the original surfaces.
        /// </param>
        /// <param name="tolerance">The offset tolerance.</param>
        /// <param name="outBlends">The results of the calculation.</param>
        /// <param name="outWalls">The results of the calculation.</param>
        /// <returns>
        /// Array of Breps if successful. If the function succeeds in offsetting, a
        /// single Brep will be returned. Otherwise, the array will contain the 
        /// offset surfaces, outBlends will contain the set of blends used to fill
        /// in gaps (if extend is false), and outWalls will contain the set of wall
        /// surfaces that was supposed to join the offset to the original (if solid
        /// is true).
        /// </returns>
        public static Brep[] CreateOffsetBrep(Brep brep, double distance, bool solid, bool extend, double tolerance, out Brep[] outBlends, out Brep[] outWalls)
        {
            return ComputeServer.Post<Brep[], Brep[], Brep[]>(ApiAddress(), out outBlends, out outWalls, brep, distance, solid, extend, tolerance);
        }

        /// <summary>
        /// Offsets a Brep.
        /// </summary>
        /// <param name="brep">The Brep to offset.</param>
        /// <param name="distance">
        /// The distance to offset. This is a signed distance value with respect to
        /// face normals and flipped faces.
        /// </param>
        /// <param name="solid">
        /// If true, then the function makes a closed solid from the input and offset
        /// surfaces by lofting a ruled surface between all of the matching edges.
        /// </param>
        /// <param name="extend">
        /// If true, then the function maintains the sharp corners when the original
        /// surfaces have sharps corner. If False, then the function creates fillets
        /// at sharp corners in the original surfaces.
        /// </param>
        /// <param name="tolerance">The offset tolerance.</param>
        /// <param name="outBlends">The results of the calculation.</param>
        /// <param name="outWalls">The results of the calculation.</param>
        /// <returns>
        /// Array of Breps if successful. If the function succeeds in offsetting, a
        /// single Brep will be returned. Otherwise, the array will contain the 
        /// offset surfaces, outBlends will contain the set of blends used to fill
        /// in gaps (if extend is false), and outWalls will contain the set of wall
        /// surfaces that was supposed to join the offset to the original (if solid
        /// is true).
        /// </returns>
        public static Brep[] CreateOffsetBrep(Remote<Brep> brep, double distance, bool solid, bool extend, double tolerance, out Brep[] outBlends, out Brep[] outWalls)
        {
            return ComputeServer.Post<Brep[], Brep[], Brep[]>(ApiAddress(), out outBlends, out outWalls, brep, distance, solid, extend, tolerance);
        }

        /// <summary>
        /// Recursively removes any Brep face with a naked edge. This function is only useful for non-manifold Breps.
        /// </summary>
        /// <returns>true if successful, false if everything is removed or if the result has any Brep edges with more than two Brep trims.
        /// </returns>
        public static bool RemoveFins(this Brep brep, out Brep updatedInstance)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep);
        }

        /// <summary>
        /// Recursively removes any Brep face with a naked edge. This function is only useful for non-manifold Breps.
        /// </summary>
        /// <returns>true if successful, false if everything is removed or if the result has any Brep edges with more than two Brep trims.
        /// </returns>
        public static bool RemoveFins(Remote<Brep> brep, out Brep updatedInstance)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep);
        }

        /// <summary>
        /// Joins two naked edges, or edges that are coincident or close together, from two Breps.
        /// </summary>
        /// <param name="brep0">The first Brep.</param>
        /// <param name="edgeIndex0">The edge index on the first Brep.</param>
        /// <param name="brep1">The second Brep.</param>
        /// <param name="edgeIndex1">The edge index on the second Brep.</param>
        /// <param name="joinTolerance">The join tolerance.</param>
        /// <returns>The resulting Brep if successful, null on failure.</returns>
        public static Brep CreateFromJoinedEdges(Brep brep0, int edgeIndex0, Brep brep1, int edgeIndex1, double joinTolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brep0, edgeIndex0, brep1, edgeIndex1, joinTolerance);
        }

        /// <summary>
        /// Joins two naked edges, or edges that are coincident or close together, from two Breps.
        /// </summary>
        /// <param name="brep0">The first Brep.</param>
        /// <param name="edgeIndex0">The edge index on the first Brep.</param>
        /// <param name="brep1">The second Brep.</param>
        /// <param name="edgeIndex1">The edge index on the second Brep.</param>
        /// <param name="joinTolerance">The join tolerance.</param>
        /// <returns>The resulting Brep if successful, null on failure.</returns>
        public static Brep CreateFromJoinedEdges(Remote<Brep> brep0, int edgeIndex0, Remote<Brep> brep1, int edgeIndex1, double joinTolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brep0, edgeIndex0, brep1, edgeIndex1, joinTolerance);
        }

        /// <summary>
        /// Constructs one or more Breps by lofting through a set of curves.
        /// </summary>
        /// <param name="curves">
        /// The curves to loft through. This function will not perform any curve sorting. You must pass in
        /// curves in the order you want them lofted. This function will not adjust the directions of open
        /// curves. Use Curve.DoDirectionsMatch and Curve.Reverse to adjust the directions of open curves.
        /// This function will not adjust the seams of closed curves. Use Curve.ChangeClosedCurveSeam to
        /// adjust the seam of closed curves.
        /// </param>
        /// <param name="start">
        /// Optional starting point of loft. Use Point3d.Unset if you do not want to include a start point.
        /// </param>
        /// <param name="end">
        /// Optional ending point of loft. Use Point3d.Unset if you do not want to include an end point.
        /// </param>
        /// <param name="loftType">type of loft to perform.</param>
        /// <param name="closed">true if the last curve in this loft should be connected back to the first one.</param>
        /// <returns>
        /// Constructs a closed surface, continuing the surface past the last curve around to the
        /// first curve. Available when you have selected three shape curves.
        /// </returns>
        /// <example>
        /// <code source='examples\vbnet\ex_loft.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_loft.cs' lang='cs'/>
        /// <code source='examples\py\ex_loft.py' lang='py'/>
        /// </example>
        public static Brep[] CreateFromLoft(IEnumerable<Curve> curves, Point3d start, Point3d end, LoftType loftType, bool closed)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), curves, start, end, loftType, closed);
        }

        /// <summary>
        /// Constructs one or more Breps by lofting through a set of curves.
        /// </summary>
        /// <param name="curves">
        /// The curves to loft through. This function will not perform any curve sorting. You must pass in
        /// curves in the order you want them lofted. This function will not adjust the directions of open
        /// curves. Use Curve.DoDirectionsMatch and Curve.Reverse to adjust the directions of open curves.
        /// This function will not adjust the seams of closed curves. Use Curve.ChangeClosedCurveSeam to
        /// adjust the seam of closed curves.
        /// </param>
        /// <param name="start">
        /// Optional starting point of loft. Use Point3d.Unset if you do not want to include a start point.
        /// </param>
        /// <param name="end">
        /// Optional ending point of loft. Use Point3d.Unset if you do not want to include an end point.
        /// </param>
        /// <param name="loftType">type of loft to perform.</param>
        /// <param name="closed">true if the last curve in this loft should be connected back to the first one.</param>
        /// <returns>
        /// Constructs a closed surface, continuing the surface past the last curve around to the
        /// first curve. Available when you have selected three shape curves.
        /// </returns>
        /// <example>
        /// <code source='examples\vbnet\ex_loft.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_loft.cs' lang='cs'/>
        /// <code source='examples\py\ex_loft.py' lang='py'/>
        /// </example>
        public static Brep[] CreateFromLoft(Remote<IEnumerable<Curve>> curves, Point3d start, Point3d end, LoftType loftType, bool closed)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), curves, start, end, loftType, closed);
        }

        /// <summary>
        /// Constructs one or more Breps by lofting through a set of curves. Input for the loft is simplified by
        /// rebuilding to a specified number of control points.
        /// </summary>
        /// <param name="curves">
        /// The curves to loft through. This function will not perform any curve sorting. You must pass in
        /// curves in the order you want them lofted. This function will not adjust the directions of open
        /// curves. Use Curve.DoDirectionsMatch and Curve.Reverse to adjust the directions of open curves.
        /// This function will not adjust the seams of closed curves. Use Curve.ChangeClosedCurveSeam to
        /// adjust the seam of closed curves.
        /// </param>
        /// <param name="start">
        /// Optional starting point of loft. Use Point3d.Unset if you do not want to include a start point.
        /// </param>
        /// <param name="end">
        /// Optional ending point of lost. Use Point3d.Unset if you do not want to include an end point.
        /// </param>
        /// <param name="loftType">type of loft to perform.</param>
        /// <param name="closed">true if the last curve in this loft should be connected back to the first one.</param>
        /// <param name="rebuildPointCount">A number of points to use while rebuilding the curves. 0 leaves turns this parameter off.</param>
        /// <returns>
        /// Constructs a closed surface, continuing the surface past the last curve around to the
        /// first curve. Available when you have selected three shape curves.
        /// </returns>
        public static Brep[] CreateFromLoftRebuild(IEnumerable<Curve> curves, Point3d start, Point3d end, LoftType loftType, bool closed, int rebuildPointCount)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), curves, start, end, loftType, closed, rebuildPointCount);
        }

        /// <summary>
        /// Constructs one or more Breps by lofting through a set of curves. Input for the loft is simplified by
        /// rebuilding to a specified number of control points.
        /// </summary>
        /// <param name="curves">
        /// The curves to loft through. This function will not perform any curve sorting. You must pass in
        /// curves in the order you want them lofted. This function will not adjust the directions of open
        /// curves. Use Curve.DoDirectionsMatch and Curve.Reverse to adjust the directions of open curves.
        /// This function will not adjust the seams of closed curves. Use Curve.ChangeClosedCurveSeam to
        /// adjust the seam of closed curves.
        /// </param>
        /// <param name="start">
        /// Optional starting point of loft. Use Point3d.Unset if you do not want to include a start point.
        /// </param>
        /// <param name="end">
        /// Optional ending point of lost. Use Point3d.Unset if you do not want to include an end point.
        /// </param>
        /// <param name="loftType">type of loft to perform.</param>
        /// <param name="closed">true if the last curve in this loft should be connected back to the first one.</param>
        /// <param name="rebuildPointCount">A number of points to use while rebuilding the curves. 0 leaves turns this parameter off.</param>
        /// <returns>
        /// Constructs a closed surface, continuing the surface past the last curve around to the
        /// first curve. Available when you have selected three shape curves.
        /// </returns>
        public static Brep[] CreateFromLoftRebuild(Remote<IEnumerable<Curve>> curves, Point3d start, Point3d end, LoftType loftType, bool closed, int rebuildPointCount)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), curves, start, end, loftType, closed, rebuildPointCount);
        }

        /// <summary>
        /// Constructs one or more Breps by lofting through a set of curves. Input for the loft is simplified by
        /// refitting to a specified tolerance.
        /// </summary>
        /// <param name="curves">
        /// The curves to loft through. This function will not perform any curve sorting. You must pass in
        /// curves in the order you want them lofted. This function will not adjust the directions of open
        /// curves. Use Curve.DoDirectionsMatch and Curve.Reverse to adjust the directions of open curves.
        /// This function will not adjust the seams of closed curves. Use Curve.ChangeClosedCurveSeam to
        /// adjust the seam of closed curves.
        /// </param>
        /// <param name="start">
        /// Optional starting point of loft. Use Point3d.Unset if you do not want to include a start point.
        /// </param>
        /// <param name="end">
        /// Optional ending point of lost. Use Point3d.Unset if you do not want to include an end point.
        /// </param>
        /// <param name="loftType">type of loft to perform.</param>
        /// <param name="closed">true if the last curve in this loft should be connected back to the first one.</param>
        /// <param name="refitTolerance">A distance to use in refitting, or 0 if you want to turn this parameter off.</param>
        /// <returns>
        /// Constructs a closed surface, continuing the surface past the last curve around to the
        /// first curve. Available when you have selected three shape curves.
        /// </returns>
        public static Brep[] CreateFromLoftRefit(IEnumerable<Curve> curves, Point3d start, Point3d end, LoftType loftType, bool closed, double refitTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), curves, start, end, loftType, closed, refitTolerance);
        }

        /// <summary>
        /// Constructs one or more Breps by lofting through a set of curves. Input for the loft is simplified by
        /// refitting to a specified tolerance.
        /// </summary>
        /// <param name="curves">
        /// The curves to loft through. This function will not perform any curve sorting. You must pass in
        /// curves in the order you want them lofted. This function will not adjust the directions of open
        /// curves. Use Curve.DoDirectionsMatch and Curve.Reverse to adjust the directions of open curves.
        /// This function will not adjust the seams of closed curves. Use Curve.ChangeClosedCurveSeam to
        /// adjust the seam of closed curves.
        /// </param>
        /// <param name="start">
        /// Optional starting point of loft. Use Point3d.Unset if you do not want to include a start point.
        /// </param>
        /// <param name="end">
        /// Optional ending point of lost. Use Point3d.Unset if you do not want to include an end point.
        /// </param>
        /// <param name="loftType">type of loft to perform.</param>
        /// <param name="closed">true if the last curve in this loft should be connected back to the first one.</param>
        /// <param name="refitTolerance">A distance to use in refitting, or 0 if you want to turn this parameter off.</param>
        /// <returns>
        /// Constructs a closed surface, continuing the surface past the last curve around to the
        /// first curve. Available when you have selected three shape curves.
        /// </returns>
        public static Brep[] CreateFromLoftRefit(Remote<IEnumerable<Curve>> curves, Point3d start, Point3d end, LoftType loftType, bool closed, double refitTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), curves, start, end, loftType, closed, refitTolerance);
        }

        /// <summary>
        /// Constructs one or more Breps by lofting through a set of curves, optionally matching start and
        /// end tangents of surfaces when first and/or last loft curves are surface edges
        /// </summary>
        /// <param name="curves">
        /// The curves to loft through. This function will not perform any curve sorting. You must pass in
        /// curves in the order you want them lofted. This function will not adjust the directions of open
        /// curves. Use Curve.DoDirectionsMatch and Curve.Reverse to adjust the directions of open curves.
        /// This function will not adjust the seams of closed curves. Use Curve.ChangeClosedCurveSeam to
        /// adjust the seam of closed curves.
        /// </param>
        /// <param name="start">
        /// Optional starting point of loft. Use Point3d.Unset if you do not want to include a start point.
        /// "start" and "StartTangent" cannot both be true.
        /// </param>
        /// <param name="end">
        /// Optional ending point of loft. Use Point3d.Unset if you do not want to include an end point.
        /// "end and "EndTangent" cannot both be true.
        /// </param>
        /// <param name="StartTangent">
        /// If StartTangent is true and the first loft curve is a surface edge, the loft will match the tangent 
        /// of the surface behind that edge.
        /// </param>
        /// <param name="EndTangent">
        /// If EndTangent is true and the first loft curve is a surface edge, the loft will match the tangent 
        /// of the surface behind that edge.
        /// </param>
        /// <param name="StartTrim">
        /// BrepTrim from the surface edge where start tangent is to be matched
        /// </param>
        /// <param name="EndTrim">
        /// BrepTrim from the surface edge where end tangent is to be matched
        /// </param>
        /// <param name="loftType">type of loft to perform.</param>
        /// <param name="closed">true if the last curve in this loft should be connected back to the first one.</param>
        /// <returns>
        /// Constructs a closed surface, continuing the surface past the last curve around to the
        /// first curve. Available when you have selected three shape curves.
        /// </returns>
        public static Brep[] CreateFromLoft(IEnumerable<Curve> curves, Point3d start, Point3d end, bool StartTangent, bool EndTangent, BrepTrim StartTrim, BrepTrim EndTrim, LoftType loftType, bool closed)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), curves, start, end, StartTangent, EndTangent, StartTrim, EndTrim, loftType, closed);
        }

        /// <summary>
        /// Constructs one or more Breps by lofting through a set of curves, optionally matching start and
        /// end tangents of surfaces when first and/or last loft curves are surface edges
        /// </summary>
        /// <param name="curves">
        /// The curves to loft through. This function will not perform any curve sorting. You must pass in
        /// curves in the order you want them lofted. This function will not adjust the directions of open
        /// curves. Use Curve.DoDirectionsMatch and Curve.Reverse to adjust the directions of open curves.
        /// This function will not adjust the seams of closed curves. Use Curve.ChangeClosedCurveSeam to
        /// adjust the seam of closed curves.
        /// </param>
        /// <param name="start">
        /// Optional starting point of loft. Use Point3d.Unset if you do not want to include a start point.
        /// "start" and "StartTangent" cannot both be true.
        /// </param>
        /// <param name="end">
        /// Optional ending point of loft. Use Point3d.Unset if you do not want to include an end point.
        /// "end and "EndTangent" cannot both be true.
        /// </param>
        /// <param name="StartTangent">
        /// If StartTangent is true and the first loft curve is a surface edge, the loft will match the tangent 
        /// of the surface behind that edge.
        /// </param>
        /// <param name="EndTangent">
        /// If EndTangent is true and the first loft curve is a surface edge, the loft will match the tangent 
        /// of the surface behind that edge.
        /// </param>
        /// <param name="StartTrim">
        /// BrepTrim from the surface edge where start tangent is to be matched
        /// </param>
        /// <param name="EndTrim">
        /// BrepTrim from the surface edge where end tangent is to be matched
        /// </param>
        /// <param name="loftType">type of loft to perform.</param>
        /// <param name="closed">true if the last curve in this loft should be connected back to the first one.</param>
        /// <returns>
        /// Constructs a closed surface, continuing the surface past the last curve around to the
        /// first curve. Available when you have selected three shape curves.
        /// </returns>
        public static Brep[] CreateFromLoft(Remote<IEnumerable<Curve>> curves, Point3d start, Point3d end, bool StartTangent, bool EndTangent, BrepTrim StartTrim, BrepTrim EndTrim, LoftType loftType, bool closed)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), curves, start, end, StartTangent, EndTangent, StartTrim, EndTrim, loftType, closed);
        }

        /// <summary>
        /// CreatePlanarUnion
        /// </summary>
        /// <param name="breps">The planar regions on which to preform the union operation.</param>
        /// <param name="plane">The plane in which all the input breps lie</param>
        /// <param name="tolerance">Tolerance to use for union operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        public static Brep[] CreatePlanarUnion(IEnumerable<Brep> breps, Plane plane, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), breps, plane, tolerance);
        }

        /// <summary>
        /// CreatePlanarUnion
        /// </summary>
        /// <param name="breps">The planar regions on which to preform the union operation.</param>
        /// <param name="plane">The plane in which all the input breps lie</param>
        /// <param name="tolerance">Tolerance to use for union operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        public static Brep[] CreatePlanarUnion(Remote<IEnumerable<Brep>> breps, Plane plane, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), breps, plane, tolerance);
        }

        /// <summary>
        /// CreatePlanarUnion
        /// </summary>
        /// <param name="b0">The first brep to union.</param>
        /// <param name="b1">The first brep to union.</param>
        /// <param name="plane">The plane in which all the input breps lie</param>
        /// <param name="tolerance">Tolerance to use for union operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        public static Brep[] CreatePlanarUnion(Brep b0, Brep b1, Plane plane, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), b0, b1, plane, tolerance);
        }

        /// <summary>
        /// CreatePlanarUnion
        /// </summary>
        /// <param name="b0">The first brep to union.</param>
        /// <param name="b1">The first brep to union.</param>
        /// <param name="plane">The plane in which all the input breps lie</param>
        /// <param name="tolerance">Tolerance to use for union operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        public static Brep[] CreatePlanarUnion(Remote<Brep> b0, Remote<Brep> b1, Plane plane, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), b0, b1, plane, tolerance);
        }

        /// <summary>
        /// CreatePlanarDifference
        /// </summary>
        /// <param name="b0">The first brep to intersect.</param>
        /// <param name="b1">The first brep to intersect.</param>
        /// <param name="plane">The plane in which all the input breps lie</param>
        /// <param name="tolerance">Tolerance to use for Difference operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        public static Brep[] CreatePlanarDifference(Brep b0, Brep b1, Plane plane, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), b0, b1, plane, tolerance);
        }

        /// <summary>
        /// CreatePlanarDifference
        /// </summary>
        /// <param name="b0">The first brep to intersect.</param>
        /// <param name="b1">The first brep to intersect.</param>
        /// <param name="plane">The plane in which all the input breps lie</param>
        /// <param name="tolerance">Tolerance to use for Difference operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        public static Brep[] CreatePlanarDifference(Remote<Brep> b0, Remote<Brep> b1, Plane plane, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), b0, b1, plane, tolerance);
        }

        /// <summary>
        /// CreatePlanarIntersection
        /// </summary>
        /// <param name="b0">The first brep to intersect.</param>
        /// <param name="b1">The first brep to intersect.</param>
        /// <param name="plane">The plane in which all the input breps lie</param>
        /// <param name="tolerance">Tolerance to use for intersection operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        public static Brep[] CreatePlanarIntersection(Brep b0, Brep b1, Plane plane, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), b0, b1, plane, tolerance);
        }

        /// <summary>
        /// CreatePlanarIntersection
        /// </summary>
        /// <param name="b0">The first brep to intersect.</param>
        /// <param name="b1">The first brep to intersect.</param>
        /// <param name="plane">The plane in which all the input breps lie</param>
        /// <param name="tolerance">Tolerance to use for intersection operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        public static Brep[] CreatePlanarIntersection(Remote<Brep> b0, Remote<Brep> b1, Plane plane, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), b0, b1, plane, tolerance);
        }

        /// <summary>
        /// Compute the Boolean Union of a set of Breps.
        /// </summary>
        /// <param name="breps">Breps to union.</param>
        /// <param name="tolerance">Tolerance to use for union operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        public static Brep[] CreateBooleanUnion(IEnumerable<Brep> breps, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), breps, tolerance);
        }

        /// <summary>
        /// Compute the Boolean Union of a set of Breps.
        /// </summary>
        /// <param name="breps">Breps to union.</param>
        /// <param name="tolerance">Tolerance to use for union operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        public static Brep[] CreateBooleanUnion(Remote<IEnumerable<Brep>> breps, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), breps, tolerance);
        }

        /// <summary>
        /// Compute the Boolean Union of a set of Breps.
        /// </summary>
        /// <param name="breps">Breps to union.</param>
        /// <param name="tolerance">Tolerance to use for union operation.</param>
        /// <param name="manifoldOnly">If true, non-manifold input breps are ignored.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        public static Brep[] CreateBooleanUnion(IEnumerable<Brep> breps, double tolerance, bool manifoldOnly)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), breps, tolerance, manifoldOnly);
        }

        /// <summary>
        /// Compute the Boolean Union of a set of Breps.
        /// </summary>
        /// <param name="breps">Breps to union.</param>
        /// <param name="tolerance">Tolerance to use for union operation.</param>
        /// <param name="manifoldOnly">If true, non-manifold input breps are ignored.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        public static Brep[] CreateBooleanUnion(Remote<IEnumerable<Brep>> breps, double tolerance, bool manifoldOnly)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), breps, tolerance, manifoldOnly);
        }

        /// <summary>
        /// Compute the Solid Intersection of two sets of Breps.
        /// </summary>
        /// <param name="firstSet">First set of Breps.</param>
        /// <param name="secondSet">Second set of Breps.</param>
        /// <param name="tolerance">Tolerance to use for intersection operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        public static Brep[] CreateBooleanIntersection(IEnumerable<Brep> firstSet, IEnumerable<Brep> secondSet, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstSet, secondSet, tolerance);
        }

        /// <summary>
        /// Compute the Solid Intersection of two sets of Breps.
        /// </summary>
        /// <param name="firstSet">First set of Breps.</param>
        /// <param name="secondSet">Second set of Breps.</param>
        /// <param name="tolerance">Tolerance to use for intersection operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        public static Brep[] CreateBooleanIntersection(Remote<IEnumerable<Brep>> firstSet, Remote<IEnumerable<Brep>> secondSet, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstSet, secondSet, tolerance);
        }

        /// <summary>
        /// Compute the Solid Intersection of two sets of Breps.
        /// </summary>
        /// <param name="firstSet">First set of Breps.</param>
        /// <param name="secondSet">Second set of Breps.</param>
        /// <param name="tolerance">Tolerance to use for intersection operation.</param>
        /// <param name="manifoldOnly">If true, non-manifold input breps are ignored.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        public static Brep[] CreateBooleanIntersection(IEnumerable<Brep> firstSet, IEnumerable<Brep> secondSet, double tolerance, bool manifoldOnly)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstSet, secondSet, tolerance, manifoldOnly);
        }

        /// <summary>
        /// Compute the Solid Intersection of two sets of Breps.
        /// </summary>
        /// <param name="firstSet">First set of Breps.</param>
        /// <param name="secondSet">Second set of Breps.</param>
        /// <param name="tolerance">Tolerance to use for intersection operation.</param>
        /// <param name="manifoldOnly">If true, non-manifold input breps are ignored.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        public static Brep[] CreateBooleanIntersection(Remote<IEnumerable<Brep>> firstSet, Remote<IEnumerable<Brep>> secondSet, double tolerance, bool manifoldOnly)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstSet, secondSet, tolerance, manifoldOnly);
        }

        /// <summary>
        /// Compute the Solid Intersection of two Breps.
        /// </summary>
        /// <param name="firstBrep">First Brep for boolean intersection.</param>
        /// <param name="secondBrep">Second Brep for boolean intersection.</param>
        /// <param name="tolerance">Tolerance to use for intersection operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        public static Brep[] CreateBooleanIntersection(Brep firstBrep, Brep secondBrep, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstBrep, secondBrep, tolerance);
        }

        /// <summary>
        /// Compute the Solid Intersection of two Breps.
        /// </summary>
        /// <param name="firstBrep">First Brep for boolean intersection.</param>
        /// <param name="secondBrep">Second Brep for boolean intersection.</param>
        /// <param name="tolerance">Tolerance to use for intersection operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        public static Brep[] CreateBooleanIntersection(Remote<Brep> firstBrep, Remote<Brep> secondBrep, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstBrep, secondBrep, tolerance);
        }

        /// <summary>
        /// Compute the Solid Intersection of two Breps.
        /// </summary>
        /// <param name="firstBrep">First Brep for boolean intersection.</param>
        /// <param name="secondBrep">Second Brep for boolean intersection.</param>
        /// <param name="tolerance">Tolerance to use for intersection operation.</param>
        /// <param name="manifoldOnly">If true, non-manifold input breps are ignored.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        public static Brep[] CreateBooleanIntersection(Brep firstBrep, Brep secondBrep, double tolerance, bool manifoldOnly)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstBrep, secondBrep, tolerance, manifoldOnly);
        }

        /// <summary>
        /// Compute the Solid Intersection of two Breps.
        /// </summary>
        /// <param name="firstBrep">First Brep for boolean intersection.</param>
        /// <param name="secondBrep">Second Brep for boolean intersection.</param>
        /// <param name="tolerance">Tolerance to use for intersection operation.</param>
        /// <param name="manifoldOnly">If true, non-manifold input breps are ignored.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        public static Brep[] CreateBooleanIntersection(Remote<Brep> firstBrep, Remote<Brep> secondBrep, double tolerance, bool manifoldOnly)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstBrep, secondBrep, tolerance, manifoldOnly);
        }

        /// <summary>
        /// Compute the Solid Difference of two sets of Breps.
        /// </summary>
        /// <param name="firstSet">First set of Breps (the set to subtract from).</param>
        /// <param name="secondSet">Second set of Breps (the set to subtract).</param>
        /// <param name="tolerance">Tolerance to use for difference operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        /// <example>
        /// <code source='examples\vbnet\ex_booleandifference.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_booleandifference.cs' lang='cs'/>
        /// <code source='examples\py\ex_booleandifference.py' lang='py'/>
        /// </example>
        public static Brep[] CreateBooleanDifference(IEnumerable<Brep> firstSet, IEnumerable<Brep> secondSet, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstSet, secondSet, tolerance);
        }

        /// <summary>
        /// Compute the Solid Difference of two sets of Breps.
        /// </summary>
        /// <param name="firstSet">First set of Breps (the set to subtract from).</param>
        /// <param name="secondSet">Second set of Breps (the set to subtract).</param>
        /// <param name="tolerance">Tolerance to use for difference operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        /// <example>
        /// <code source='examples\vbnet\ex_booleandifference.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_booleandifference.cs' lang='cs'/>
        /// <code source='examples\py\ex_booleandifference.py' lang='py'/>
        /// </example>
        public static Brep[] CreateBooleanDifference(Remote<IEnumerable<Brep>> firstSet, Remote<IEnumerable<Brep>> secondSet, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstSet, secondSet, tolerance);
        }

        /// <summary>
        /// Compute the Solid Difference of two sets of Breps.
        /// </summary>
        /// <param name="firstSet">First set of Breps (the set to subtract from).</param>
        /// <param name="secondSet">Second set of Breps (the set to subtract).</param>
        /// <param name="tolerance">Tolerance to use for difference operation.</param>
        /// <param name="manifoldOnly">If true, non-manifold input breps are ignored.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        /// <example>
        /// <code source='examples\vbnet\ex_booleandifference.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_booleandifference.cs' lang='cs'/>
        /// <code source='examples\py\ex_booleandifference.py' lang='py'/>
        /// </example>
        public static Brep[] CreateBooleanDifference(IEnumerable<Brep> firstSet, IEnumerable<Brep> secondSet, double tolerance, bool manifoldOnly)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstSet, secondSet, tolerance, manifoldOnly);
        }

        /// <summary>
        /// Compute the Solid Difference of two sets of Breps.
        /// </summary>
        /// <param name="firstSet">First set of Breps (the set to subtract from).</param>
        /// <param name="secondSet">Second set of Breps (the set to subtract).</param>
        /// <param name="tolerance">Tolerance to use for difference operation.</param>
        /// <param name="manifoldOnly">If true, non-manifold input breps are ignored.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        /// <example>
        /// <code source='examples\vbnet\ex_booleandifference.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_booleandifference.cs' lang='cs'/>
        /// <code source='examples\py\ex_booleandifference.py' lang='py'/>
        /// </example>
        public static Brep[] CreateBooleanDifference(Remote<IEnumerable<Brep>> firstSet, Remote<IEnumerable<Brep>> secondSet, double tolerance, bool manifoldOnly)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstSet, secondSet, tolerance, manifoldOnly);
        }

        /// <summary>
        /// Compute the Solid Difference of two Breps.
        /// </summary>
        /// <param name="firstBrep">First Brep for boolean difference.</param>
        /// <param name="secondBrep">Second Brep for boolean difference.</param>
        /// <param name="tolerance">Tolerance to use for difference operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        public static Brep[] CreateBooleanDifference(Brep firstBrep, Brep secondBrep, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstBrep, secondBrep, tolerance);
        }

        /// <summary>
        /// Compute the Solid Difference of two Breps.
        /// </summary>
        /// <param name="firstBrep">First Brep for boolean difference.</param>
        /// <param name="secondBrep">Second Brep for boolean difference.</param>
        /// <param name="tolerance">Tolerance to use for difference operation.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        public static Brep[] CreateBooleanDifference(Remote<Brep> firstBrep, Remote<Brep> secondBrep, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstBrep, secondBrep, tolerance);
        }

        /// <summary>
        /// Compute the Solid Difference of two Breps.
        /// </summary>
        /// <param name="firstBrep">First Brep for boolean difference.</param>
        /// <param name="secondBrep">Second Brep for boolean difference.</param>
        /// <param name="tolerance">Tolerance to use for difference operation.</param>
        /// <param name="manifoldOnly">If true, non-manifold input breps are ignored.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        public static Brep[] CreateBooleanDifference(Brep firstBrep, Brep secondBrep, double tolerance, bool manifoldOnly)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstBrep, secondBrep, tolerance, manifoldOnly);
        }

        /// <summary>
        /// Compute the Solid Difference of two Breps.
        /// </summary>
        /// <param name="firstBrep">First Brep for boolean difference.</param>
        /// <param name="secondBrep">Second Brep for boolean difference.</param>
        /// <param name="tolerance">Tolerance to use for difference operation.</param>
        /// <param name="manifoldOnly">If true, non-manifold input breps are ignored.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        /// <remarks>The solid orientation of the breps make a difference when calling this function</remarks>
        public static Brep[] CreateBooleanDifference(Remote<Brep> firstBrep, Remote<Brep> secondBrep, double tolerance, bool manifoldOnly)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstBrep, secondBrep, tolerance, manifoldOnly);
        }

        /// <summary>
        /// Splits shared areas of Breps and creates separate Breps from the shared and unshared parts.
        /// </summary>
        /// <param name="firstBrep">The Brep to split.</param>
        /// <param name="secondBrep">The cutting Brep.</param>
        /// <param name="tolerance">Tolerance to use for splitting operation. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>An array of Brep if successful, an empty array on failure.</returns>
        public static Brep[] CreateBooleanSplit(Brep firstBrep, Brep secondBrep, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstBrep, secondBrep, tolerance);
        }

        /// <summary>
        /// Splits shared areas of Breps and creates separate Breps from the shared and unshared parts.
        /// </summary>
        /// <param name="firstBrep">The Brep to split.</param>
        /// <param name="secondBrep">The cutting Brep.</param>
        /// <param name="tolerance">Tolerance to use for splitting operation. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>An array of Brep if successful, an empty array on failure.</returns>
        public static Brep[] CreateBooleanSplit(Remote<Brep> firstBrep, Remote<Brep> secondBrep, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstBrep, secondBrep, tolerance);
        }

        /// <summary>
        /// Splits shared areas of Breps and creates separate Breps from the shared and unshared parts.
        /// </summary>
        /// <param name="firstSet">The Breps to split.</param>
        /// <param name="secondSet">The cutting Breps.</param>
        /// <param name="tolerance">Tolerance to use for splitting operation. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>An array of Brep if successful, an empty array on failure.</returns>
        public static Brep[] CreateBooleanSplit(IEnumerable<Brep> firstSet, IEnumerable<Brep> secondSet, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstSet, secondSet, tolerance);
        }

        /// <summary>
        /// Splits shared areas of Breps and creates separate Breps from the shared and unshared parts.
        /// </summary>
        /// <param name="firstSet">The Breps to split.</param>
        /// <param name="secondSet">The cutting Breps.</param>
        /// <param name="tolerance">Tolerance to use for splitting operation. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>An array of Brep if successful, an empty array on failure.</returns>
        public static Brep[] CreateBooleanSplit(Remote<IEnumerable<Brep>> firstSet, Remote<IEnumerable<Brep>> secondSet, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), firstSet, secondSet, tolerance);
        }

        /// <summary>
        /// Creates a hollowed out shell from a solid Brep. Function only operates on simple, solid, manifold Breps.
        /// </summary>
        /// <param name="brep">The solid Brep to shell.</param>
        /// <param name="facesToRemove">The indices of the Brep faces to remove. These surfaces are removed and the remainder is offset inward, using the outer parts of the removed surfaces to join the inner and outer parts.</param>
        /// <param name="distance">The distance, or thickness, for the shell. This is a signed distance value with respect to face normals and flipped faces.</param>
        /// <param name="tolerance">The offset tolerance. When in doubt, use the document's absolute tolerance.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        public static Brep[] CreateShell(Brep brep, IEnumerable<int> facesToRemove, double distance, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, facesToRemove, distance, tolerance);
        }

        /// <summary>
        /// Creates a hollowed out shell from a solid Brep. Function only operates on simple, solid, manifold Breps.
        /// </summary>
        /// <param name="brep">The solid Brep to shell.</param>
        /// <param name="facesToRemove">The indices of the Brep faces to remove. These surfaces are removed and the remainder is offset inward, using the outer parts of the removed surfaces to join the inner and outer parts.</param>
        /// <param name="distance">The distance, or thickness, for the shell. This is a signed distance value with respect to face normals and flipped faces.</param>
        /// <param name="tolerance">The offset tolerance. When in doubt, use the document's absolute tolerance.</param>
        /// <returns>An array of Brep results or null on failure.</returns>
        public static Brep[] CreateShell(Remote<Brep> brep, IEnumerable<int> facesToRemove, double distance, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, facesToRemove, distance, tolerance);
        }

        /// <summary>
        /// Joins the breps in the input array at any overlapping edges to form
        /// as few as possible resulting breps. There may be more than one brep in the result array.
        /// </summary>
        /// <param name="brepsToJoin">A list, an array or any enumerable set of breps to join.</param>
        /// <param name="tolerance">3d distance tolerance for detecting overlapping edges.</param>
        /// <returns>new joined breps on success, null on failure.</returns>
        public static Brep[] JoinBreps(IEnumerable<Brep> brepsToJoin, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brepsToJoin, tolerance);
        }

        /// <summary>
        /// Joins the breps in the input array at any overlapping edges to form
        /// as few as possible resulting breps. There may be more than one brep in the result array.
        /// </summary>
        /// <param name="brepsToJoin">A list, an array or any enumerable set of breps to join.</param>
        /// <param name="tolerance">3d distance tolerance for detecting overlapping edges.</param>
        /// <returns>new joined breps on success, null on failure.</returns>
        public static Brep[] JoinBreps(Remote<IEnumerable<Brep>> brepsToJoin, double tolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brepsToJoin, tolerance);
        }

        /// <summary>
        /// Combines two or more breps into one. A merge is like a boolean union that keeps the inside pieces. This
        /// function creates non-manifold Breps which in general are unusual in Rhino. You may want to consider using
        /// JoinBreps or CreateBooleanUnion functions instead.
        /// </summary>
        /// <param name="brepsToMerge">must contain more than one Brep.</param>
        /// <param name="tolerance">the tolerance to use when merging.</param>
        /// <returns>Single merged Brep on success. Null on error.</returns>
        /// <seealso cref="JoinBreps"/>
        public static Brep MergeBreps(IEnumerable<Brep> brepsToMerge, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brepsToMerge, tolerance);
        }

        /// <summary>
        /// Combines two or more breps into one. A merge is like a boolean union that keeps the inside pieces. This
        /// function creates non-manifold Breps which in general are unusual in Rhino. You may want to consider using
        /// JoinBreps or CreateBooleanUnion functions instead.
        /// </summary>
        /// <param name="brepsToMerge">must contain more than one Brep.</param>
        /// <param name="tolerance">the tolerance to use when merging.</param>
        /// <returns>Single merged Brep on success. Null on error.</returns>
        /// <seealso cref="JoinBreps"/>
        public static Brep MergeBreps(Remote<IEnumerable<Brep>> brepsToMerge, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brepsToMerge, tolerance);
        }

        /// <summary>
        /// Constructs the contour curves for a brep at a specified interval.
        /// </summary>
        /// <param name="brepToContour">A brep or polysurface.</param>
        /// <param name="contourStart">A point to start.</param>
        /// <param name="contourEnd">A point to use as the end.</param>
        /// <param name="interval">The interaxial offset in world units.</param>
        /// <returns>An array with intersected curves. This array can be empty.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_makerhinocontours.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_makerhinocontours.cs' lang='cs'/>
        /// <code source='examples\py\ex_makerhinocontours.py' lang='py'/>
        /// </example>
        public static Curve[] CreateContourCurves(Brep brepToContour, Point3d contourStart, Point3d contourEnd, double interval)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), brepToContour, contourStart, contourEnd, interval);
        }

        /// <summary>
        /// Constructs the contour curves for a brep at a specified interval.
        /// </summary>
        /// <param name="brepToContour">A brep or polysurface.</param>
        /// <param name="contourStart">A point to start.</param>
        /// <param name="contourEnd">A point to use as the end.</param>
        /// <param name="interval">The interaxial offset in world units.</param>
        /// <returns>An array with intersected curves. This array can be empty.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_makerhinocontours.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_makerhinocontours.cs' lang='cs'/>
        /// <code source='examples\py\ex_makerhinocontours.py' lang='py'/>
        /// </example>
        public static Curve[] CreateContourCurves(Remote<Brep> brepToContour, Point3d contourStart, Point3d contourEnd, double interval)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), brepToContour, contourStart, contourEnd, interval);
        }

        /// <summary>
        /// Constructs the contour curves for a brep, using a slicing plane.
        /// </summary>
        /// <param name="brepToContour">A brep or polysurface.</param>
        /// <param name="sectionPlane">A plane.</param>
        /// <returns>An array with intersected curves. This array can be empty.</returns>
        public static Curve[] CreateContourCurves(Brep brepToContour, Plane sectionPlane)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), brepToContour, sectionPlane);
        }

        /// <summary>
        /// Constructs the contour curves for a brep, using a slicing plane.
        /// </summary>
        /// <param name="brepToContour">A brep or polysurface.</param>
        /// <param name="sectionPlane">A plane.</param>
        /// <returns>An array with intersected curves. This array can be empty.</returns>
        public static Curve[] CreateContourCurves(Remote<Brep> brepToContour, Plane sectionPlane)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), brepToContour, sectionPlane);
        }

        /// <summary>
        /// Create an array of analysis meshes for the brep using the specified settings.
        /// Meshes aren't set on the brep.
        /// </summary>
        /// <param name="brep"></param>
        /// <param name="state"> CurvatureAnalysisSettingsState </param>
        /// <returns>true if meshes were created</returns>
        public static Mesh[] CreateCurvatureAnalysisMesh(Brep brep, Rhino.ApplicationSettings.CurvatureAnalysisSettingsState state)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), brep, state);
        }

        /// <summary>
        /// Create an array of analysis meshes for the brep using the specified settings.
        /// Meshes aren't set on the brep.
        /// </summary>
        /// <param name="brep"></param>
        /// <param name="state"> CurvatureAnalysisSettingsState </param>
        /// <returns>true if meshes were created</returns>
        public static Mesh[] CreateCurvatureAnalysisMesh(Remote<Brep> brep, Rhino.ApplicationSettings.CurvatureAnalysisSettingsState state)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), brep, state);
        }

        /// <summary>
        /// Gets an array containing all regions in this brep.
        /// </summary>
        /// <returns>An array of regions in this brep. This array can be empty, but not null.</returns>
        public static BrepRegion[] GetRegions(this Brep brep)
        {
            return ComputeServer.Post<BrepRegion[]>(ApiAddress(), brep);
        }

        /// <summary>
        /// Gets an array containing all regions in this brep.
        /// </summary>
        /// <returns>An array of regions in this brep. This array can be empty, but not null.</returns>
        public static BrepRegion[] GetRegions(Remote<Brep> brep)
        {
            return ComputeServer.Post<BrepRegion[]>(ApiAddress(), brep);
        }

        /// <summary>
        /// Constructs all the Wireframe curves for this Brep.
        /// </summary>
        /// <param name="density">Wireframe density. Valid values range between -1 and 99.</param>
        /// <returns>An array of Wireframe curves or null on failure.</returns>
        public static Curve[] GetWireframe(this Brep brep, int density)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), brep, density);
        }

        /// <summary>
        /// Constructs all the Wireframe curves for this Brep.
        /// </summary>
        /// <param name="density">Wireframe density. Valid values range between -1 and 99.</param>
        /// <returns>An array of Wireframe curves or null on failure.</returns>
        public static Curve[] GetWireframe(Remote<Brep> brep, int density)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), brep, density);
        }

        /// <summary>
        /// Finds a point on the brep that is closest to testPoint.
        /// </summary>
        /// <param name="testPoint">Base point to project to brep.</param>
        /// <returns>The point on the Brep closest to testPoint or Point3d.Unset if the operation failed.</returns>
        public static Point3d ClosestPoint(this Brep brep, Point3d testPoint)
        {
            return ComputeServer.Post<Point3d>(ApiAddress(), brep, testPoint);
        }

        /// <summary>
        /// Finds a point on the brep that is closest to testPoint.
        /// </summary>
        /// <param name="testPoint">Base point to project to brep.</param>
        /// <returns>The point on the Brep closest to testPoint or Point3d.Unset if the operation failed.</returns>
        public static Point3d ClosestPoint(Remote<Brep> brep, Point3d testPoint)
        {
            return ComputeServer.Post<Point3d>(ApiAddress(), brep, testPoint);
        }

        /// <summary>
        /// Determines if point is inside a Brep.  This question only makes sense when
        /// the brep is a closed and manifold.  This function does not check for
        /// closed or manifold, so result is not valid in those cases.  Intersects
        /// a line through point with brep, finds the intersection point Q closest
        /// to point, and looks at face normal at Q.  If the point Q is on an edge
        /// or the intersection is not transverse at Q, then another line is used.
        /// </summary>
        /// <param name="point">3d point to test.</param>
        /// <param name="tolerance">
        /// 3d distance tolerance used for intersection and determining strict inclusion.
        /// A good default is RhinoMath.SqrtEpsilon.
        /// </param>
        /// <param name="strictlyIn">
        /// if true, point is in if inside brep by at least tolerance.
        /// if false, point is in if truly in or within tolerance of boundary.
        /// </param>
        /// <returns>
        /// true if point is in, false if not.
        /// </returns>
        public static bool IsPointInside(this Brep brep, Point3d point, double tolerance, bool strictlyIn)
        {
            return ComputeServer.Post<bool>(ApiAddress(), brep, point, tolerance, strictlyIn);
        }

        /// <summary>
        /// Determines if point is inside a Brep.  This question only makes sense when
        /// the brep is a closed and manifold.  This function does not check for
        /// closed or manifold, so result is not valid in those cases.  Intersects
        /// a line through point with brep, finds the intersection point Q closest
        /// to point, and looks at face normal at Q.  If the point Q is on an edge
        /// or the intersection is not transverse at Q, then another line is used.
        /// </summary>
        /// <param name="point">3d point to test.</param>
        /// <param name="tolerance">
        /// 3d distance tolerance used for intersection and determining strict inclusion.
        /// A good default is RhinoMath.SqrtEpsilon.
        /// </param>
        /// <param name="strictlyIn">
        /// if true, point is in if inside brep by at least tolerance.
        /// if false, point is in if truly in or within tolerance of boundary.
        /// </param>
        /// <returns>
        /// true if point is in, false if not.
        /// </returns>
        public static bool IsPointInside(Remote<Brep> brep, Point3d point, double tolerance, bool strictlyIn)
        {
            return ComputeServer.Post<bool>(ApiAddress(), brep, point, tolerance, strictlyIn);
        }

        /// <summary>
        /// Finds a point inside of a solid Brep.
        /// </summary>
        /// <param name="tolerance">
        /// Used for intersecting rays and is not necessarily related to the distance from the brep to the found point.
        /// When in doubt, use the document's model absolute tolerance.
        /// </param>
        /// <param name="point">A point inside the solid Brep.</param>
        /// <returns>
        /// Returns false if the input is not solid and manifold, if the Brep's bounding box is less than 2.0 * tolerance wide, 
        /// or if no point could be found due to ray shooting or other errors. Otherwise, true is returned.
        /// </returns>
        public static bool GetPointInside(this Brep brep, double tolerance, out Point3d point)
        {
            return ComputeServer.Post<bool, Point3d>(ApiAddress(), out point, brep, tolerance);
        }

        /// <summary>
        /// Finds a point inside of a solid Brep.
        /// </summary>
        /// <param name="tolerance">
        /// Used for intersecting rays and is not necessarily related to the distance from the brep to the found point.
        /// When in doubt, use the document's model absolute tolerance.
        /// </param>
        /// <param name="point">A point inside the solid Brep.</param>
        /// <returns>
        /// Returns false if the input is not solid and manifold, if the Brep's bounding box is less than 2.0 * tolerance wide, 
        /// or if no point could be found due to ray shooting or other errors. Otherwise, true is returned.
        /// </returns>
        public static bool GetPointInside(Remote<Brep> brep, double tolerance, out Point3d point)
        {
            return ComputeServer.Post<bool, Point3d>(ApiAddress(), out point, brep, tolerance);
        }

        /// <summary>
        /// Returns a new Brep that is equivalent to this Brep with all planar holes capped.
        /// </summary>
        /// <param name="tolerance">Tolerance to use for capping.</param>
        /// <returns>New brep on success. null on error.</returns>
        public static Brep CapPlanarHoles(this Brep brep, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brep, tolerance);
        }

        /// <summary>
        /// Returns a new Brep that is equivalent to this Brep with all planar holes capped.
        /// </summary>
        /// <param name="tolerance">Tolerance to use for capping.</param>
        /// <returns>New brep on success. null on error.</returns>
        public static Brep CapPlanarHoles(Remote<Brep> brep, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brep, tolerance);
        }

        /// <summary>
        /// If any edges of this brep overlap edges of otherBrep, merge a copy of otherBrep into this
        /// brep joining all edges that overlap within tolerance.
        /// </summary>
        /// <param name="otherBrep">Brep to be added to this brep.</param>
        /// <param name="tolerance">3d distance tolerance for detecting overlapping edges.</param>
        /// <param name="compact">if true, set brep flags and tolerances, remove unused faces and edges.</param>
        /// <returns>true if any edges were joined.</returns>
        /// <remarks>
        /// if no edges overlap, this brep is unchanged.
        /// otherBrep is copied if it is merged with this, and otherBrep is always unchanged
        /// Use this to join a list of breps in a series.
        /// When joining multiple breps in series, compact should be set to false.
        /// Call compact on the last Join.
        /// </remarks>
        public static bool Join(this Brep brep, out Brep updatedInstance, Brep otherBrep, double tolerance, bool compact)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep, otherBrep, tolerance, compact);
        }

        /// <summary>
        /// If any edges of this brep overlap edges of otherBrep, merge a copy of otherBrep into this
        /// brep joining all edges that overlap within tolerance.
        /// </summary>
        /// <param name="otherBrep">Brep to be added to this brep.</param>
        /// <param name="tolerance">3d distance tolerance for detecting overlapping edges.</param>
        /// <param name="compact">if true, set brep flags and tolerances, remove unused faces and edges.</param>
        /// <returns>true if any edges were joined.</returns>
        /// <remarks>
        /// if no edges overlap, this brep is unchanged.
        /// otherBrep is copied if it is merged with this, and otherBrep is always unchanged
        /// Use this to join a list of breps in a series.
        /// When joining multiple breps in series, compact should be set to false.
        /// Call compact on the last Join.
        /// </remarks>
        public static bool Join(Remote<Brep> brep, out Brep updatedInstance, Remote<Brep> otherBrep, double tolerance, bool compact)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep, otherBrep, tolerance, compact);
        }

        /// <summary>
        /// Joins naked edge pairs within the same brep that overlap within tolerance.
        /// </summary>
        /// <param name="tolerance">The tolerance value.</param>
        /// <returns>number of joins made.</returns>
        public static int JoinNakedEdges(this Brep brep, out Brep updatedInstance, double tolerance)
        {
            return ComputeServer.Post<int, Brep>(ApiAddress(), out updatedInstance, brep, tolerance);
        }

        /// <summary>
        /// Joins naked edge pairs within the same brep that overlap within tolerance.
        /// </summary>
        /// <param name="tolerance">The tolerance value.</param>
        /// <returns>number of joins made.</returns>
        public static int JoinNakedEdges(Remote<Brep> brep, out Brep updatedInstance, double tolerance)
        {
            return ComputeServer.Post<int, Brep>(ApiAddress(), out updatedInstance, brep, tolerance);
        }

        /// <summary>
        /// Merges adjacent coplanar faces into single faces.
        /// </summary>
        /// <param name="tolerance">
        /// Tolerance for determining when edges are adjacent.
        /// When in doubt, use the document's ModelAbsoluteTolerance property.
        /// </param>
        /// <returns>true if faces were merged, false if no faces were merged.</returns>
        public static bool MergeCoplanarFaces(this Brep brep, out Brep updatedInstance, double tolerance)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep, tolerance);
        }

        /// <summary>
        /// Merges adjacent coplanar faces into single faces.
        /// </summary>
        /// <param name="tolerance">
        /// Tolerance for determining when edges are adjacent.
        /// When in doubt, use the document's ModelAbsoluteTolerance property.
        /// </param>
        /// <returns>true if faces were merged, false if no faces were merged.</returns>
        public static bool MergeCoplanarFaces(Remote<Brep> brep, out Brep updatedInstance, double tolerance)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep, tolerance);
        }

        /// <summary>
        /// Merges adjacent coplanar faces into single faces.
        /// </summary>
        /// <param name="tolerance">
        /// Tolerance for determining when edges are adjacent.
        /// When in doubt, use the document's ModelAbsoluteTolerance property.
        /// </param>
        /// <param name="angleTolerance">
        /// Angle tolerance, in radians, for determining when faces are parallel.
        /// When in doubt, use the document's ModelAngleToleranceRadians property.
        /// </param>
        /// <returns>true if faces were merged, false if no faces were merged.</returns>
        public static bool MergeCoplanarFaces(this Brep brep, out Brep updatedInstance, double tolerance, double angleTolerance)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep, tolerance, angleTolerance);
        }

        /// <summary>
        /// Merges adjacent coplanar faces into single faces.
        /// </summary>
        /// <param name="tolerance">
        /// Tolerance for determining when edges are adjacent.
        /// When in doubt, use the document's ModelAbsoluteTolerance property.
        /// </param>
        /// <param name="angleTolerance">
        /// Angle tolerance, in radians, for determining when faces are parallel.
        /// When in doubt, use the document's ModelAngleToleranceRadians property.
        /// </param>
        /// <returns>true if faces were merged, false if no faces were merged.</returns>
        public static bool MergeCoplanarFaces(Remote<Brep> brep, out Brep updatedInstance, double tolerance, double angleTolerance)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep, tolerance, angleTolerance);
        }

        /// <summary>
        /// Splits a Brep into pieces using a Brep as a cutter.
        /// </summary>
        /// <param name="cutter">The Brep to use as a cutter.</param>
        /// <param name="intersectionTolerance">The tolerance with which to compute intersections.</param>
        /// <returns>A new array of Breps. This array can be empty.</returns>
        public static Brep[] Split(this Brep brep, Brep cutter, double intersectionTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, cutter, intersectionTolerance);
        }

        /// <summary>
        /// Splits a Brep into pieces using a Brep as a cutter.
        /// </summary>
        /// <param name="cutter">The Brep to use as a cutter.</param>
        /// <param name="intersectionTolerance">The tolerance with which to compute intersections.</param>
        /// <returns>A new array of Breps. This array can be empty.</returns>
        public static Brep[] Split(Remote<Brep> brep, Remote<Brep> cutter, double intersectionTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, cutter, intersectionTolerance);
        }

        /// <summary>
        /// Splits a Brep into pieces using a Brep as a cutter.
        /// </summary>
        /// <param name="cutter">The Brep to use as a cutter.</param>
        /// <param name="intersectionTolerance">The tolerance with which to compute intersections.</param>
        /// <param name="toleranceWasRaised">
        /// Set to true if the split failed at intersectionTolerance but succeeded
        /// when the tolerance was increased to twice intersectionTolerance.
        /// </param>
        /// <returns>A new array of Breps. This array can be empty.</returns>
        public static Brep[] Split(this Brep brep, Brep cutter, double intersectionTolerance, out bool toleranceWasRaised)
        {
            return ComputeServer.Post<Brep[], bool>(ApiAddress(), out toleranceWasRaised, brep, cutter, intersectionTolerance);
        }

        /// <summary>
        /// Splits a Brep into pieces using a Brep as a cutter.
        /// </summary>
        /// <param name="cutter">The Brep to use as a cutter.</param>
        /// <param name="intersectionTolerance">The tolerance with which to compute intersections.</param>
        /// <param name="toleranceWasRaised">
        /// Set to true if the split failed at intersectionTolerance but succeeded
        /// when the tolerance was increased to twice intersectionTolerance.
        /// </param>
        /// <returns>A new array of Breps. This array can be empty.</returns>
        public static Brep[] Split(Remote<Brep> brep, Remote<Brep> cutter, double intersectionTolerance, out bool toleranceWasRaised)
        {
            return ComputeServer.Post<Brep[], bool>(ApiAddress(), out toleranceWasRaised, brep, cutter, intersectionTolerance);
        }

        /// <summary>
        /// Splits a Brep into pieces using Breps as cutters.
        /// </summary>
        /// <param name="cutters">One or more Breps to use as cutters.</param>
        /// <param name="intersectionTolerance">The tolerance with which to compute intersections.</param>
        /// <returns>A new array of Breps. This array can be empty.</returns>
        public static Brep[] Split(this Brep brep, IEnumerable<Brep> cutters, double intersectionTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, cutters, intersectionTolerance);
        }

        /// <summary>
        /// Splits a Brep into pieces using Breps as cutters.
        /// </summary>
        /// <param name="cutters">One or more Breps to use as cutters.</param>
        /// <param name="intersectionTolerance">The tolerance with which to compute intersections.</param>
        /// <returns>A new array of Breps. This array can be empty.</returns>
        public static Brep[] Split(Remote<Brep> brep, Remote<IEnumerable<Brep>> cutters, double intersectionTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, cutters, intersectionTolerance);
        }

        /// <summary>
        /// Splits a Brep into pieces using curves, at least partially on the Brep, as cutters.
        /// </summary>
        /// <param name="cutters">The splitting curves. Only the portion of the curve on the Brep surface will be used for cutting.</param>
        /// <param name="intersectionTolerance">The tolerance with which to compute intersections.</param>
        /// <returns>A new array of Breps. This array can be empty.</returns>
        public static Brep[] Split(this Brep brep, IEnumerable<Curve> cutters, double intersectionTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, cutters, intersectionTolerance);
        }

        /// <summary>
        /// Splits a Brep into pieces using curves, at least partially on the Brep, as cutters.
        /// </summary>
        /// <param name="cutters">The splitting curves. Only the portion of the curve on the Brep surface will be used for cutting.</param>
        /// <param name="intersectionTolerance">The tolerance with which to compute intersections.</param>
        /// <returns>A new array of Breps. This array can be empty.</returns>
        public static Brep[] Split(Remote<Brep> brep, Remote<IEnumerable<Curve>> cutters, double intersectionTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, cutters, intersectionTolerance);
        }

        /// <summary>
        /// Splits a Brep into pieces using a combination of curves, to be extruded, and Breps as cutters.
        /// </summary>
        /// <param name="cutters">The curves, surfaces, faces and Breps to be used as cutters. Any other geometry is ignored.</param>
        /// <param name="normal">A construction plane normal, used in deciding how to extrude a curve into a cutter.</param>
        /// <param name="planView">Set true if the assume view is a plan, or parallel projection, view.</param>
        /// <param name="intersectionTolerance">The tolerance with which to compute intersections.</param>
        /// <returns>A new array of Breps. This array can be empty.</returns>
        /// <remarks>
        /// A Curve in cutters is extruded to produce a surface to use as a cutter. The extrusion direction is chosen, as in the Rhino Split command,
        /// based on the properties of the active view. In particular the construction plane Normal and whether the active view is a plan view, 
        /// a parallel projection with construction plane normal as the view direction. If planView is false and the curve is planar then the curve
        /// is extruded perpendicular to the curve, otherwise the curve is extruded in the normal direction.
        /// </remarks>
        public static Brep[] Split(this Brep brep, IEnumerable<GeometryBase> cutters, Vector3d normal, bool planView, double intersectionTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, cutters, normal, planView, intersectionTolerance);
        }

        /// <summary>
        /// Splits a Brep into pieces using a combination of curves, to be extruded, and Breps as cutters.
        /// </summary>
        /// <param name="cutters">The curves, surfaces, faces and Breps to be used as cutters. Any other geometry is ignored.</param>
        /// <param name="normal">A construction plane normal, used in deciding how to extrude a curve into a cutter.</param>
        /// <param name="planView">Set true if the assume view is a plan, or parallel projection, view.</param>
        /// <param name="intersectionTolerance">The tolerance with which to compute intersections.</param>
        /// <returns>A new array of Breps. This array can be empty.</returns>
        /// <remarks>
        /// A Curve in cutters is extruded to produce a surface to use as a cutter. The extrusion direction is chosen, as in the Rhino Split command,
        /// based on the properties of the active view. In particular the construction plane Normal and whether the active view is a plan view, 
        /// a parallel projection with construction plane normal as the view direction. If planView is false and the curve is planar then the curve
        /// is extruded perpendicular to the curve, otherwise the curve is extruded in the normal direction.
        /// </remarks>
        public static Brep[] Split(Remote<Brep> brep, Remote<IEnumerable<GeometryBase>> cutters, Vector3d normal, bool planView, double intersectionTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, cutters, normal, planView, intersectionTolerance);
        }

        /// <summary>
        /// Trims a brep with an oriented cutter. The parts of the brep that lie inside
        /// (opposite the normal) of the cutter are retained while the parts to the
        /// outside (in the direction of the normal) are discarded.  If the Cutter is
        /// closed, then a connected component of the Brep that does not intersect the
        /// cutter is kept if and only if it is contained in the inside of cutter.
        /// That is the region bounded by cutter opposite from the normal of cutter,
        /// If cutter is not closed all these components are kept.
        /// </summary>
        /// <param name="cutter">A cutting brep.</param>
        /// <param name="intersectionTolerance">A tolerance value with which to compute intersections.</param>
        /// <returns>This Brep is not modified, the trim results are returned in an array.</returns>
        public static Brep[] Trim(this Brep brep, Brep cutter, double intersectionTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, cutter, intersectionTolerance);
        }

        /// <summary>
        /// Trims a brep with an oriented cutter. The parts of the brep that lie inside
        /// (opposite the normal) of the cutter are retained while the parts to the
        /// outside (in the direction of the normal) are discarded.  If the Cutter is
        /// closed, then a connected component of the Brep that does not intersect the
        /// cutter is kept if and only if it is contained in the inside of cutter.
        /// That is the region bounded by cutter opposite from the normal of cutter,
        /// If cutter is not closed all these components are kept.
        /// </summary>
        /// <param name="cutter">A cutting brep.</param>
        /// <param name="intersectionTolerance">A tolerance value with which to compute intersections.</param>
        /// <returns>This Brep is not modified, the trim results are returned in an array.</returns>
        public static Brep[] Trim(Remote<Brep> brep, Remote<Brep> cutter, double intersectionTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, cutter, intersectionTolerance);
        }

        /// <summary>
        /// Trims a Brep with an oriented cutter.  The parts of Brep that lie inside
        /// (opposite the normal) of the cutter are retained while the parts to the
        /// outside ( in the direction of the normal ) are discarded. A connected
        /// component of Brep that does not intersect the cutter is kept if and only
        /// if it is contained in the inside of Cutter.  That is the region bounded by
        /// cutter opposite from the normal of cutter, or in the case of a Plane cutter
        /// the half space opposite from the plane normal.
        /// </summary>
        /// <param name="cutter">A cutting plane.</param>
        /// <param name="intersectionTolerance">A tolerance value with which to compute intersections.</param>
        /// <returns>This Brep is not modified, the trim results are returned in an array.</returns>
        public static Brep[] Trim(this Brep brep, Plane cutter, double intersectionTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, cutter, intersectionTolerance);
        }

        /// <summary>
        /// Trims a Brep with an oriented cutter.  The parts of Brep that lie inside
        /// (opposite the normal) of the cutter are retained while the parts to the
        /// outside ( in the direction of the normal ) are discarded. A connected
        /// component of Brep that does not intersect the cutter is kept if and only
        /// if it is contained in the inside of Cutter.  That is the region bounded by
        /// cutter opposite from the normal of cutter, or in the case of a Plane cutter
        /// the half space opposite from the plane normal.
        /// </summary>
        /// <param name="cutter">A cutting plane.</param>
        /// <param name="intersectionTolerance">A tolerance value with which to compute intersections.</param>
        /// <returns>This Brep is not modified, the trim results are returned in an array.</returns>
        public static Brep[] Trim(Remote<Brep> brep, Plane cutter, double intersectionTolerance)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, cutter, intersectionTolerance);
        }

        /// <summary>
        /// Un-joins, or separates, edges within the Brep. Note, seams in closed surfaces will not separate.
        /// </summary>
        /// <param name="edgesToUnjoin">The indices of the Brep edges to un-join.</param>
        /// <returns>This Brep is not modified, the trim results are returned in an array.</returns>
        public static Brep[] UnjoinEdges(this Brep brep, IEnumerable<int> edgesToUnjoin)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, edgesToUnjoin);
        }

        /// <summary>
        /// Un-joins, or separates, edges within the Brep. Note, seams in closed surfaces will not separate.
        /// </summary>
        /// <param name="edgesToUnjoin">The indices of the Brep edges to un-join.</param>
        /// <returns>This Brep is not modified, the trim results are returned in an array.</returns>
        public static Brep[] UnjoinEdges(Remote<Brep> brep, IEnumerable<int> edgesToUnjoin)
        {
            return ComputeServer.Post<Brep[]>(ApiAddress(), brep, edgesToUnjoin);
        }

        /// <summary>
        /// Joins two naked edges, or edges that are coincident or close together.
        /// </summary>
        /// <param name="edgeIndex0">The first edge index.</param>
        /// <param name="edgeIndex1">The second edge index.</param>
        /// <param name="joinTolerance">The join tolerance.</param>
        /// <param name="compact">
        /// If joining more than one edge pair and want the edge indices of subsequent pairs to remain valid, 
        /// set to false. But then call Brep.Compact() on the final result.
        /// </param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool JoinEdges(this Brep brep, out Brep updatedInstance, int edgeIndex0, int edgeIndex1, double joinTolerance, bool compact)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep, edgeIndex0, edgeIndex1, joinTolerance, compact);
        }

        /// <summary>
        /// Joins two naked edges, or edges that are coincident or close together.
        /// </summary>
        /// <param name="edgeIndex0">The first edge index.</param>
        /// <param name="edgeIndex1">The second edge index.</param>
        /// <param name="joinTolerance">The join tolerance.</param>
        /// <param name="compact">
        /// If joining more than one edge pair and want the edge indices of subsequent pairs to remain valid, 
        /// set to false. But then call Brep.Compact() on the final result.
        /// </param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool JoinEdges(Remote<Brep> brep, out Brep updatedInstance, int edgeIndex0, int edgeIndex1, double joinTolerance, bool compact)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep, edgeIndex0, edgeIndex1, joinTolerance, compact);
        }

        /// <summary>
        /// Transform an array of Brep components, bend neighbors to match, and leave the rest fixed.
        /// </summary>
        /// <param name="components">The Brep components to transform.</param>
        /// <param name="xform">The transformation to apply.</param>
        /// <param name="tolerance">The desired fitting tolerance to use when bending faces that share edges with both fixed and transformed components.</param>
        /// <param name="timeLimit">
        /// If the deformation is extreme, it can take a long time to calculate the result.
        /// If time_limit > 0, then the value specifies the maximum amount of time in seconds you want to spend before giving up.
        /// </param>
        /// <param name="useMultipleThreads">True if multiple threads can be used.</param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool TransformComponent(this Brep brep, out Brep updatedInstance, IEnumerable<ComponentIndex> components, Transform xform, double tolerance, double timeLimit, bool useMultipleThreads)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep, components, xform, tolerance, timeLimit, useMultipleThreads);
        }

        /// <summary>
        /// Transform an array of Brep components, bend neighbors to match, and leave the rest fixed.
        /// </summary>
        /// <param name="components">The Brep components to transform.</param>
        /// <param name="xform">The transformation to apply.</param>
        /// <param name="tolerance">The desired fitting tolerance to use when bending faces that share edges with both fixed and transformed components.</param>
        /// <param name="timeLimit">
        /// If the deformation is extreme, it can take a long time to calculate the result.
        /// If time_limit > 0, then the value specifies the maximum amount of time in seconds you want to spend before giving up.
        /// </param>
        /// <param name="useMultipleThreads">True if multiple threads can be used.</param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool TransformComponent(Remote<Brep> brep, out Brep updatedInstance, IEnumerable<ComponentIndex> components, Transform xform, double tolerance, double timeLimit, bool useMultipleThreads)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep, components, xform, tolerance, timeLimit, useMultipleThreads);
        }

        /// <summary>
        /// Compute the Area of the Brep. If you want proper Area data with moments 
        /// and error information, use the AreaMassProperties class.
        /// </summary>
        /// <returns>The area of the Brep.</returns>
        public static double GetArea(this Brep brep)
        {
            return ComputeServer.Post<double>(ApiAddress(), brep);
        }

        /// <summary>
        /// Compute the Area of the Brep. If you want proper Area data with moments 
        /// and error information, use the AreaMassProperties class.
        /// </summary>
        /// <returns>The area of the Brep.</returns>
        public static double GetArea(Remote<Brep> brep)
        {
            return ComputeServer.Post<double>(ApiAddress(), brep);
        }

        /// <summary>
        /// Compute the Area of the Brep. If you want proper Area data with moments 
        /// and error information, use the AreaMassProperties class.
        /// </summary>
        /// <param name="relativeTolerance">Relative tolerance to use for area calculation.</param>
        /// <param name="absoluteTolerance">Absolute tolerance to use for area calculation.</param>
        /// <returns>The area of the Brep.</returns>
        public static double GetArea(this Brep brep, double relativeTolerance, double absoluteTolerance)
        {
            return ComputeServer.Post<double>(ApiAddress(), brep, relativeTolerance, absoluteTolerance);
        }

        /// <summary>
        /// Compute the Area of the Brep. If you want proper Area data with moments 
        /// and error information, use the AreaMassProperties class.
        /// </summary>
        /// <param name="relativeTolerance">Relative tolerance to use for area calculation.</param>
        /// <param name="absoluteTolerance">Absolute tolerance to use for area calculation.</param>
        /// <returns>The area of the Brep.</returns>
        public static double GetArea(Remote<Brep> brep, double relativeTolerance, double absoluteTolerance)
        {
            return ComputeServer.Post<double>(ApiAddress(), brep, relativeTolerance, absoluteTolerance);
        }

        /// <summary>
        /// Compute the Volume of the Brep. If you want proper Volume data with moments 
        /// and error information, use the VolumeMassProperties class.
        /// </summary>
        /// <returns>The volume of the Brep.</returns>
        public static double GetVolume(this Brep brep)
        {
            return ComputeServer.Post<double>(ApiAddress(), brep);
        }

        /// <summary>
        /// Compute the Volume of the Brep. If you want proper Volume data with moments 
        /// and error information, use the VolumeMassProperties class.
        /// </summary>
        /// <returns>The volume of the Brep.</returns>
        public static double GetVolume(Remote<Brep> brep)
        {
            return ComputeServer.Post<double>(ApiAddress(), brep);
        }

        /// <summary>
        /// Compute the Volume of the Brep. If you want proper Volume data with moments 
        /// and error information, use the VolumeMassProperties class.
        /// </summary>
        /// <param name="relativeTolerance">Relative tolerance to use for area calculation.</param>
        /// <param name="absoluteTolerance">Absolute tolerance to use for area calculation.</param>
        /// <returns>The volume of the Brep.</returns>
        public static double GetVolume(this Brep brep, double relativeTolerance, double absoluteTolerance)
        {
            return ComputeServer.Post<double>(ApiAddress(), brep, relativeTolerance, absoluteTolerance);
        }

        /// <summary>
        /// Compute the Volume of the Brep. If you want proper Volume data with moments 
        /// and error information, use the VolumeMassProperties class.
        /// </summary>
        /// <param name="relativeTolerance">Relative tolerance to use for area calculation.</param>
        /// <param name="absoluteTolerance">Absolute tolerance to use for area calculation.</param>
        /// <returns>The volume of the Brep.</returns>
        public static double GetVolume(Remote<Brep> brep, double relativeTolerance, double absoluteTolerance)
        {
            return ComputeServer.Post<double>(ApiAddress(), brep, relativeTolerance, absoluteTolerance);
        }

        /// <summary>
        /// No support is available for this function.
        /// <para>Expert user function used by MakeValidForV2 to convert trim
        /// curves from one surface to its NURBS form. After calling this function,
        /// you need to change the surface of the face to a NurbsSurface.</para>
        /// </summary>
        /// <param name="face">
        /// Face whose underlying surface has a parameterization that is different
        /// from its NURBS form.
        /// </param>
        /// <param name="nurbsSurface">NURBS form of the face's underlying surface.</param>
        /// <remarks>
        /// Don't call this function unless you know exactly what you are doing.
        /// </remarks>
        public static Brep RebuildTrimsForV2(this Brep brep, BrepFace face, NurbsSurface nurbsSurface)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brep, face, nurbsSurface);
        }

        /// <summary>
        /// No support is available for this function.
        /// <para>Expert user function used by MakeValidForV2 to convert trim
        /// curves from one surface to its NURBS form. After calling this function,
        /// you need to change the surface of the face to a NurbsSurface.</para>
        /// </summary>
        /// <param name="face">
        /// Face whose underlying surface has a parameterization that is different
        /// from its NURBS form.
        /// </param>
        /// <param name="nurbsSurface">NURBS form of the face's underlying surface.</param>
        /// <remarks>
        /// Don't call this function unless you know exactly what you are doing.
        /// </remarks>
        public static Brep RebuildTrimsForV2(Remote<Brep> brep, BrepFace face, Remote<NurbsSurface> nurbsSurface)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brep, face, nurbsSurface);
        }

        /// <summary>
        /// No support is available for this function.
        /// <para>Expert user function that converts all geometry in Brep to NURB form.</para>
        /// </summary>
        /// <remarks>
        /// Don't call this function unless you know exactly what you are doing.
        /// </remarks>
        public static bool MakeValidForV2(this Brep brep, out Brep updatedInstance)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep);
        }

        /// <summary>
        /// No support is available for this function.
        /// <para>Expert user function that converts all geometry in Brep to NURB form.</para>
        /// </summary>
        /// <remarks>
        /// Don't call this function unless you know exactly what you are doing.
        /// </remarks>
        public static bool MakeValidForV2(Remote<Brep> brep, out Brep updatedInstance)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep);
        }

        /// <summary>
        /// Fills in missing or fixes incorrect component information from a Brep. 
        /// Useful when reading Brep information from other file formats that do not 
        /// provide as complete of a Brep definition as required by Rhino.
        /// </summary>
        /// <param name="tolerance">The repair tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>True on success.</returns>
        public static bool Repair(this Brep brep, out Brep updatedInstance, double tolerance)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep, tolerance);
        }

        /// <summary>
        /// Fills in missing or fixes incorrect component information from a Brep. 
        /// Useful when reading Brep information from other file formats that do not 
        /// provide as complete of a Brep definition as required by Rhino.
        /// </summary>
        /// <param name="tolerance">The repair tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>True on success.</returns>
        public static bool Repair(Remote<Brep> brep, out Brep updatedInstance, double tolerance)
        {
            return ComputeServer.Post<bool, Brep>(ApiAddress(), out updatedInstance, brep, tolerance);
        }

        /// <summary>
        /// Remove all inner loops, or holes, in a Brep.
        /// </summary>
        /// <param name="tolerance">The tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>The Brep without holes if successful, null otherwise.</returns>
        public static Brep RemoveHoles(this Brep brep, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brep, tolerance);
        }

        /// <summary>
        /// Remove all inner loops, or holes, in a Brep.
        /// </summary>
        /// <param name="tolerance">The tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>The Brep without holes if successful, null otherwise.</returns>
        public static Brep RemoveHoles(Remote<Brep> brep, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brep, tolerance);
        }

        /// <summary>
        /// Removes inner loops, or holes, in a Brep.
        /// </summary>
        /// <param name="loops">A list of BrepLoop component indexes, where BrepLoop.LoopType == Rhino.Geometry.BrepLoopType.Inner.</param>
        /// <param name="tolerance">The tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>The Brep without holes if successful, null otherwise.</returns>
        public static Brep RemoveHoles(this Brep brep, IEnumerable<ComponentIndex> loops, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brep, loops, tolerance);
        }

        /// <summary>
        /// Removes inner loops, or holes, in a Brep.
        /// </summary>
        /// <param name="loops">A list of BrepLoop component indexes, where BrepLoop.LoopType == Rhino.Geometry.BrepLoopType.Inner.</param>
        /// <param name="tolerance">The tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>The Brep without holes if successful, null otherwise.</returns>
        public static Brep RemoveHoles(Remote<Brep> brep, IEnumerable<ComponentIndex> loops, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brep, loops, tolerance);
        }
    }

    public static class BrepFaceCompute
    {
        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return ComputeServer.ApiAddress(typeof(BrepFace), caller);
        }

        /// <summary>
        /// Pulls one or more points to a brep face.
        /// </summary>
        /// <param name="points">Points to pull.</param>
        /// <param name="tolerance">Tolerance for pulling operation. Only points that are closer than tolerance will be pulled to the face.</param>
        /// <returns>An array of pulled points.</returns>
        public static Point3d[] PullPointsToFace(this BrepFace brepface, IEnumerable<Point3d> points, double tolerance)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), brepface, points, tolerance);
        }

        /// <summary>
        ///  Returns the surface draft angle and point at a parameter.
        /// </summary>
        /// <param name="testPoint">The u,v parameter on the face to evaluate.</param>
        /// <param name="testAngle">The angle in radians to test.</param>
        /// <param name="pullDirection">The pull direction.</param>
        /// <param name="edge">Restricts the point placement to an edge.</param>
        /// <param name="draftPoint">The draft angle point.</param>
        /// <param name="draftAngle">The draft angle in radians.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public static bool DraftAnglePoint(this BrepFace brepface, Point2d testPoint, double testAngle, Vector3d pullDirection, bool edge, out Point3d draftPoint, out double draftAngle)
        {
            return ComputeServer.Post<bool, Point3d, double>(ApiAddress(), out draftPoint, out draftAngle, brepface, testPoint, testAngle, pullDirection, edge);
        }

        /// <summary>
        /// Remove all inner loops, or holes, from a Brep face.
        /// </summary>
        /// <param name="tolerance"></param>
        /// <returns></returns>
        public static Brep RemoveHoles(this BrepFace brepface, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brepface, tolerance);
        }

        /// <summary>
/// Shrinks the underlying untrimmed surface of this Brep face right to the trimming boundaries.
/// Note, shrinking the trimmed surface can sometimes cause problems later since having
/// the edges so close to the trimming boundaries can cause commands that use the surface
/// edges as input to fail.
/// </summary>
/// <returns>true on success, false on failure.</returns>
        public static bool ShrinkSurfaceToEdge(this BrepFace brepface, out BrepFace updatedInstance)
        {
            return ComputeServer.Post<bool, BrepFace>(ApiAddress(), out updatedInstance, brepface);
        }

        /// <summary>
        /// Split this face using 3D trimming curves.
        /// </summary>
        /// <param name="curves">Curves to split with.</param>
        /// <param name="tolerance">Tolerance for splitting, when in doubt use the Document Absolute Tolerance.</param>
        /// <returns>A brep consisting of all the split fragments, or null on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_tightboundingbox.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_tightboundingbox.cs' lang='cs'/>
        /// <code source='examples\py\ex_tightboundingbox.py' lang='py'/>
        /// </example>
        public static Brep Split(this BrepFace brepface, IEnumerable<Curve> curves, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brepface, curves, tolerance);
        }

        /// <summary>
        /// Split this face using 3D trimming curves.
        /// </summary>
        /// <param name="curves">Curves to split with.</param>
        /// <param name="tolerance">Tolerance for splitting, when in doubt use the Document Absolute Tolerance.</param>
        /// <returns>A brep consisting of all the split fragments, or null on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_tightboundingbox.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_tightboundingbox.cs' lang='cs'/>
        /// <code source='examples\py\ex_tightboundingbox.py' lang='py'/>
        /// </example>
        public static Brep Split(this BrepFace brepface, Remote<IEnumerable<Curve>> curves, double tolerance)
        {
            return ComputeServer.Post<Brep>(ApiAddress(), brepface, curves, tolerance);
        }

        /// <summary>
        /// Tests if a parameter space point is in the active region of a face.
        /// </summary>
        /// <param name="u">Parameter space point U value.</param>
        /// <param name="v">Parameter space point V value.</param>
        /// <returns>A value describing the relationship between the point and the face.</returns>
        public static PointFaceRelation IsPointOnFace(this BrepFace brepface, double u, double v)
        {
            return ComputeServer.Post<PointFaceRelation>(ApiAddress(), brepface, u, v);
        }

        /// <summary>
        /// Tests if a parameter space point is in the active region of a face.
        /// </summary>
        /// <param name="u">Parameter space point U value.</param>
        /// <param name="v">Parameter space point V value.</param>
        /// <param name="tolerance">3D tolerance used when checking to see if the point is on a face or inside of a loop.</param>
        /// <returns>A value describing the relationship between the point and the face.</returns>
        public static PointFaceRelation IsPointOnFace(this BrepFace brepface, double u, double v, double tolerance)
        {
            return ComputeServer.Post<PointFaceRelation>(ApiAddress(), brepface, u, v, tolerance);
        }

        /// <summary>
        /// Gets intervals where the iso curve exists on a BrepFace (trimmed surface)
        /// </summary>
        /// <param name="direction">Direction of isocurve.
        /// <para>0 = Isocurve connects all points with a constant U value.</para>
        /// <para>1 = Isocurve connects all points with a constant V value.</para>
        /// </param>
        /// <param name="constantParameter">Surface parameter that remains identical along the isocurves.</param>
        /// <returns>
        /// If direction = 0, the parameter space iso interval connects the 2d points
        /// (intervals[i][0],iso_constant) and (intervals[i][1],iso_constant).
        /// If direction = 1, the parameter space iso interval connects the 2d points
        /// (iso_constant,intervals[i][0]) and (iso_constant,intervals[i][1]).
        /// </returns>
        public static Interval[] TrimAwareIsoIntervals(this BrepFace brepface, int direction, double constantParameter)
        {
            return ComputeServer.Post<Interval[]>(ApiAddress(), brepface, direction, constantParameter);
        }

        /// <summary>
        /// Similar to IsoCurve function, except this function pays attention to trims on faces 
        /// and may return multiple curves.
        /// </summary>
        /// <param name="direction">Direction of isocurve.
        /// <para>0 = Isocurve connects all points with a constant U value.</para>
        /// <para>1 = Isocurve connects all points with a constant V value.</para>
        /// </param>
        /// <param name="constantParameter">Surface parameter that remains identical along the isocurves.</param>
        /// <returns>Isoparametric curves connecting all points with the constantParameter value.</returns>
        /// <remarks>
        /// In this function "direction" indicates which direction the resulting curve runs.
        /// 0: horizontal, 1: vertical
        /// In the other Surface functions that take a "direction" argument,
        /// "direction" indicates if "constantParameter" is a "u" or "v" parameter.
        /// </remarks>
        public static Curve[] TrimAwareIsoCurve(this BrepFace brepface, int direction, double constantParameter)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), brepface, direction, constantParameter);
        }

        /// <summary>
        /// Expert user tool that replaces the 3d surface geometry use by the face.
        /// </summary>
        /// <param name="surfaceIndex">brep surface index of new surface.</param>
        /// <returns>true if successful.</returns>
        /// <remarks>
        /// If the face had a surface and new surface has a different shape, then
        /// you probably want to call something like RebuildEdges() to move
        /// the 3d edge curves so they will lie on the new surface. This doesn't
        /// delete the old surface; call Brep.CullUnusedSurfaces() or Brep.Compact()
        /// to remove unused surfaces.
        /// </remarks>
        public static bool ChangeSurface(this BrepFace brepface, out BrepFace updatedInstance, int surfaceIndex)
        {
            return ComputeServer.Post<bool, BrepFace>(ApiAddress(), out updatedInstance, brepface, surfaceIndex);
        }

        /// <summary>
        /// Rebuild the edges used by a face so they lie on the surface.
        /// </summary>
        /// <param name="tolerance">tolerance for fitting 3d edge curves.</param>
        /// <param name="rebuildSharedEdges">
        /// if false and edge is used by this face and a neighbor, then the edge
        /// will be skipped.
        /// </param>
        /// <param name="rebuildVertices">
        /// if true, vertex locations are updated to lie on the surface.
        /// </param>
        /// <returns>true on success.</returns>
        public static bool RebuildEdges(this BrepFace brepface, out BrepFace updatedInstance, double tolerance, bool rebuildSharedEdges, bool rebuildVertices)
        {
            return ComputeServer.Post<bool, BrepFace>(ApiAddress(), out updatedInstance, brepface, tolerance, rebuildSharedEdges, rebuildVertices);
        }
    }

    public static class SurfaceCompute
    {
        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return ComputeServer.ApiAddress(typeof(Surface), caller);
        }

        /// <summary>
        /// Constructs a rolling ball fillet between two surfaces.
        /// </summary>
        /// <param name="surfaceA">A first surface.</param>
        /// <param name="surfaceB">A second surface.</param>
        /// <param name="radius">A radius value.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>A new array of rolling ball fillet surfaces; this array can be empty on failure.</returns>
        /// <exception cref="ArgumentNullException">If surfaceA or surfaceB are null.</exception>
        public static Surface[] CreateRollingBallFillet(Surface surfaceA, Surface surfaceB, double radius, double tolerance)
        {
            return ComputeServer.Post<Surface[]>(ApiAddress(), surfaceA, surfaceB, radius, tolerance);
        }

        /// <summary>
        /// Constructs a rolling ball fillet between two surfaces.
        /// </summary>
        /// <param name="surfaceA">A first surface.</param>
        /// <param name="surfaceB">A second surface.</param>
        /// <param name="radius">A radius value.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>A new array of rolling ball fillet surfaces; this array can be empty on failure.</returns>
        /// <exception cref="ArgumentNullException">If surfaceA or surfaceB are null.</exception>
        public static Surface[] CreateRollingBallFillet(Remote<Surface> surfaceA, Remote<Surface> surfaceB, double radius, double tolerance)
        {
            return ComputeServer.Post<Surface[]>(ApiAddress(), surfaceA, surfaceB, radius, tolerance);
        }

        /// <summary>
        /// Constructs a rolling ball fillet between two surfaces.
        /// </summary>
        /// <param name="surfaceA">A first surface.</param>
        /// <param name="flipA">A value that indicates whether A should be used in flipped mode.</param>
        /// <param name="surfaceB">A second surface.</param>
        /// <param name="flipB">A value that indicates whether B should be used in flipped mode.</param>
        /// <param name="radius">A radius value.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>A new array of rolling ball fillet surfaces; this array can be empty on failure.</returns>
        /// <exception cref="ArgumentNullException">If surfaceA or surfaceB are null.</exception>
        public static Surface[] CreateRollingBallFillet(Surface surfaceA, bool flipA, Surface surfaceB, bool flipB, double radius, double tolerance)
        {
            return ComputeServer.Post<Surface[]>(ApiAddress(), surfaceA, flipA, surfaceB, flipB, radius, tolerance);
        }

        /// <summary>
        /// Constructs a rolling ball fillet between two surfaces.
        /// </summary>
        /// <param name="surfaceA">A first surface.</param>
        /// <param name="flipA">A value that indicates whether A should be used in flipped mode.</param>
        /// <param name="surfaceB">A second surface.</param>
        /// <param name="flipB">A value that indicates whether B should be used in flipped mode.</param>
        /// <param name="radius">A radius value.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>A new array of rolling ball fillet surfaces; this array can be empty on failure.</returns>
        /// <exception cref="ArgumentNullException">If surfaceA or surfaceB are null.</exception>
        public static Surface[] CreateRollingBallFillet(Remote<Surface> surfaceA, bool flipA, Remote<Surface> surfaceB, bool flipB, double radius, double tolerance)
        {
            return ComputeServer.Post<Surface[]>(ApiAddress(), surfaceA, flipA, surfaceB, flipB, radius, tolerance);
        }

        /// <summary>
        /// Constructs a rolling ball fillet between two surfaces.
        /// </summary>
        /// <param name="surfaceA">A first surface.</param>
        /// <param name="uvA">A point in the parameter space of FaceA near where the fillet is expected to hit the surface.</param>
        /// <param name="surfaceB">A second surface.</param>
        /// <param name="uvB">A point in the parameter space of FaceB near where the fillet is expected to hit the surface.</param>
        /// <param name="radius">A radius value.</param>
        /// <param name="tolerance">A tolerance value used for approximating and intersecting offset surfaces.</param>
        /// <returns>A new array of rolling ball fillet surfaces; this array can be empty on failure.</returns>
        /// <exception cref="ArgumentNullException">If surfaceA or surfaceB are null.</exception>
        public static Surface[] CreateRollingBallFillet(Surface surfaceA, Point2d uvA, Surface surfaceB, Point2d uvB, double radius, double tolerance)
        {
            return ComputeServer.Post<Surface[]>(ApiAddress(), surfaceA, uvA, surfaceB, uvB, radius, tolerance);
        }

        /// <summary>
        /// Constructs a rolling ball fillet between two surfaces.
        /// </summary>
        /// <param name="surfaceA">A first surface.</param>
        /// <param name="uvA">A point in the parameter space of FaceA near where the fillet is expected to hit the surface.</param>
        /// <param name="surfaceB">A second surface.</param>
        /// <param name="uvB">A point in the parameter space of FaceB near where the fillet is expected to hit the surface.</param>
        /// <param name="radius">A radius value.</param>
        /// <param name="tolerance">A tolerance value used for approximating and intersecting offset surfaces.</param>
        /// <returns>A new array of rolling ball fillet surfaces; this array can be empty on failure.</returns>
        /// <exception cref="ArgumentNullException">If surfaceA or surfaceB are null.</exception>
        public static Surface[] CreateRollingBallFillet(Remote<Surface> surfaceA, Point2d uvA, Remote<Surface> surfaceB, Point2d uvB, double radius, double tolerance)
        {
            return ComputeServer.Post<Surface[]>(ApiAddress(), surfaceA, uvA, surfaceB, uvB, radius, tolerance);
        }

        /// <summary>
        /// Constructs a surface by extruding a curve along a vector.
        /// </summary>
        /// <param name="profile">Profile curve to extrude.</param>
        /// <param name="direction">Direction and length of extrusion.</param>
        /// <returns>A surface on success or null on failure.</returns>
        public static Surface CreateExtrusion(Curve profile, Vector3d direction)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), profile, direction);
        }

        /// <summary>
        /// Constructs a surface by extruding a curve along a vector.
        /// </summary>
        /// <param name="profile">Profile curve to extrude.</param>
        /// <param name="direction">Direction and length of extrusion.</param>
        /// <returns>A surface on success or null on failure.</returns>
        public static Surface CreateExtrusion(Remote<Curve> profile, Vector3d direction)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), profile, direction);
        }

        /// <summary>
        /// Constructs a surface by extruding a curve to a point.
        /// </summary>
        /// <param name="profile">Profile curve to extrude.</param>
        /// <param name="apexPoint">Apex point of extrusion.</param>
        /// <returns>A Surface on success or null on failure.</returns>
        public static Surface CreateExtrusionToPoint(Curve profile, Point3d apexPoint)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), profile, apexPoint);
        }

        /// <summary>
        /// Constructs a surface by extruding a curve to a point.
        /// </summary>
        /// <param name="profile">Profile curve to extrude.</param>
        /// <param name="apexPoint">Apex point of extrusion.</param>
        /// <returns>A Surface on success or null on failure.</returns>
        public static Surface CreateExtrusionToPoint(Remote<Curve> profile, Point3d apexPoint)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), profile, apexPoint);
        }

        /// <summary>
        /// Constructs a periodic surface from a base surface and a direction.
        /// </summary>
        /// <param name="surface">The surface to make periodic.</param>
        /// <param name="direction">The direction to make periodic, either 0 = U, or 1 = V.</param>
        /// <returns>A Surface on success or null on failure.</returns>
        public static Surface CreatePeriodicSurface(Surface surface, int direction)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), surface, direction);
        }

        /// <summary>
        /// Constructs a periodic surface from a base surface and a direction.
        /// </summary>
        /// <param name="surface">The surface to make periodic.</param>
        /// <param name="direction">The direction to make periodic, either 0 = U, or 1 = V.</param>
        /// <returns>A Surface on success or null on failure.</returns>
        public static Surface CreatePeriodicSurface(Remote<Surface> surface, int direction)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), surface, direction);
        }

        /// <summary>
        /// Constructs a periodic surface from a base surface and a direction.
        /// </summary>
        /// <param name="surface">The surface to make periodic.</param>
        /// <param name="direction">The direction to make periodic, either 0 = U, or 1 = V.</param>
        /// <param name="bSmooth">
        /// Controls kink removal. If true, smooths any kinks in the surface and moves control points
        /// to make a smooth surface. If false, control point locations are not changed or changed minimally
        /// (only one point may move) and only the knot vector is altered.
        /// </param>
        /// <returns>A periodic surface if successful, null on failure.</returns>
        public static Surface CreatePeriodicSurface(Surface surface, int direction, bool bSmooth)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), surface, direction, bSmooth);
        }

        /// <summary>
        /// Constructs a periodic surface from a base surface and a direction.
        /// </summary>
        /// <param name="surface">The surface to make periodic.</param>
        /// <param name="direction">The direction to make periodic, either 0 = U, or 1 = V.</param>
        /// <param name="bSmooth">
        /// Controls kink removal. If true, smooths any kinks in the surface and moves control points
        /// to make a smooth surface. If false, control point locations are not changed or changed minimally
        /// (only one point may move) and only the knot vector is altered.
        /// </param>
        /// <returns>A periodic surface if successful, null on failure.</returns>
        public static Surface CreatePeriodicSurface(Remote<Surface> surface, int direction, bool bSmooth)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), surface, direction, bSmooth);
        }

        /// <summary>
        /// Creates a soft edited surface from an existing surface using a smooth field of influence.
        /// </summary>
        /// <param name="surface">The surface to soft edit.</param>
        /// <param name="uv">
        /// A point in the parameter space to move from. This location on the surface is moved, 
        /// and the move is smoothly tapered off with increasing distance along the surface from
        /// this parameter.
        /// </param>
        /// <param name="delta">The direction and magnitude, or maximum distance, of the move.</param>
        /// <param name="uLength">
        /// The distance along the surface's u-direction from the editing point over which the
        /// strength of the editing falls off smoothly.
        /// </param>
        /// <param name="vLength">
        /// The distance along the surface's v-direction from the editing point over which the
        /// strength of the editing falls off smoothly.
        /// </param>
        /// <param name="tolerance">The active document's model absolute tolerance.</param>
        /// <param name="fixEnds">Keeps edge locations fixed.</param>
        /// <returns>The soft edited surface if successful. null on failure.</returns>
        public static Surface CreateSoftEditSurface(Surface surface, Point2d uv, Vector3d delta, double uLength, double vLength, double tolerance, bool fixEnds)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), surface, uv, delta, uLength, vLength, tolerance, fixEnds);
        }

        /// <summary>
        /// Creates a soft edited surface from an existing surface using a smooth field of influence.
        /// </summary>
        /// <param name="surface">The surface to soft edit.</param>
        /// <param name="uv">
        /// A point in the parameter space to move from. This location on the surface is moved, 
        /// and the move is smoothly tapered off with increasing distance along the surface from
        /// this parameter.
        /// </param>
        /// <param name="delta">The direction and magnitude, or maximum distance, of the move.</param>
        /// <param name="uLength">
        /// The distance along the surface's u-direction from the editing point over which the
        /// strength of the editing falls off smoothly.
        /// </param>
        /// <param name="vLength">
        /// The distance along the surface's v-direction from the editing point over which the
        /// strength of the editing falls off smoothly.
        /// </param>
        /// <param name="tolerance">The active document's model absolute tolerance.</param>
        /// <param name="fixEnds">Keeps edge locations fixed.</param>
        /// <returns>The soft edited surface if successful. null on failure.</returns>
        public static Surface CreateSoftEditSurface(Remote<Surface> surface, Point2d uv, Vector3d delta, double uLength, double vLength, double tolerance, bool fixEnds)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), surface, uv, delta, uLength, vLength, tolerance, fixEnds);
        }

        /// <summary>
        /// Smooths a surface by averaging the positions of control points in a specified region.
        /// </summary>
        /// <param name="smoothFactor">The smoothing factor, which controls how much control points move towards the average of the neighboring control points.</param>
        /// <param name="bXSmooth">When true control points move in X axis direction.</param>
        /// <param name="bYSmooth">When true control points move in Y axis direction.</param>
        /// <param name="bZSmooth">When true control points move in Z axis direction.</param>
        /// <param name="bFixBoundaries">When true the surface edges don't move.</param>
        /// <param name="coordinateSystem">The coordinates to determine the direction of the smoothing.</param>
        /// <returns>The smoothed surface if successful, null otherwise.</returns>
        public static Surface Smooth(this Surface surface, out Surface updatedInstance, double smoothFactor, bool bXSmooth, bool bYSmooth, bool bZSmooth, bool bFixBoundaries, SmoothingCoordinateSystem coordinateSystem)
        {
            return ComputeServer.Post<Surface, Surface>(ApiAddress(), out updatedInstance, surface, smoothFactor, bXSmooth, bYSmooth, bZSmooth, bFixBoundaries, coordinateSystem);
        }

        /// <summary>
        /// Smooths a surface by averaging the positions of control points in a specified region.
        /// </summary>
        /// <param name="smoothFactor">The smoothing factor, which controls how much control points move towards the average of the neighboring control points.</param>
        /// <param name="bXSmooth">When true control points move in X axis direction.</param>
        /// <param name="bYSmooth">When true control points move in Y axis direction.</param>
        /// <param name="bZSmooth">When true control points move in Z axis direction.</param>
        /// <param name="bFixBoundaries">When true the surface edges don't move.</param>
        /// <param name="coordinateSystem">The coordinates to determine the direction of the smoothing.</param>
        /// <returns>The smoothed surface if successful, null otherwise.</returns>
        public static Surface Smooth(Remote<Surface> surface, out Surface updatedInstance, double smoothFactor, bool bXSmooth, bool bYSmooth, bool bZSmooth, bool bFixBoundaries, SmoothingCoordinateSystem coordinateSystem)
        {
            return ComputeServer.Post<Surface, Surface>(ApiAddress(), out updatedInstance, surface, smoothFactor, bXSmooth, bYSmooth, bZSmooth, bFixBoundaries, coordinateSystem);
        }

        /// <summary>
        /// Smooths a surface by averaging the positions of control points in a specified region.
        /// </summary>
        /// <param name="smoothFactor">The smoothing factor, which controls how much control points move towards the average of the neighboring control points.</param>
        /// <param name="bXSmooth">When true control points move in X axis direction.</param>
        /// <param name="bYSmooth">When true control points move in Y axis direction.</param>
        /// <param name="bZSmooth">When true control points move in Z axis direction.</param>
        /// <param name="bFixBoundaries">When true the surface edges don't move.</param>
        /// <param name="coordinateSystem">The coordinates to determine the direction of the smoothing.</param>
        /// <param name="plane">If SmoothingCoordinateSystem.CPlane specified, then the construction plane.</param>
        /// <returns>The smoothed surface if successful, null otherwise.</returns>
        public static Surface Smooth(this Surface surface, out Surface updatedInstance, double smoothFactor, bool bXSmooth, bool bYSmooth, bool bZSmooth, bool bFixBoundaries, SmoothingCoordinateSystem coordinateSystem, Plane plane)
        {
            return ComputeServer.Post<Surface, Surface>(ApiAddress(), out updatedInstance, surface, smoothFactor, bXSmooth, bYSmooth, bZSmooth, bFixBoundaries, coordinateSystem, plane);
        }

        /// <summary>
        /// Smooths a surface by averaging the positions of control points in a specified region.
        /// </summary>
        /// <param name="smoothFactor">The smoothing factor, which controls how much control points move towards the average of the neighboring control points.</param>
        /// <param name="bXSmooth">When true control points move in X axis direction.</param>
        /// <param name="bYSmooth">When true control points move in Y axis direction.</param>
        /// <param name="bZSmooth">When true control points move in Z axis direction.</param>
        /// <param name="bFixBoundaries">When true the surface edges don't move.</param>
        /// <param name="coordinateSystem">The coordinates to determine the direction of the smoothing.</param>
        /// <param name="plane">If SmoothingCoordinateSystem.CPlane specified, then the construction plane.</param>
        /// <returns>The smoothed surface if successful, null otherwise.</returns>
        public static Surface Smooth(Remote<Surface> surface, out Surface updatedInstance, double smoothFactor, bool bXSmooth, bool bYSmooth, bool bZSmooth, bool bFixBoundaries, SmoothingCoordinateSystem coordinateSystem, Plane plane)
        {
            return ComputeServer.Post<Surface, Surface>(ApiAddress(), out updatedInstance, surface, smoothFactor, bXSmooth, bYSmooth, bZSmooth, bFixBoundaries, coordinateSystem, plane);
        }

        /// <summary>
        /// Copies a surface so that all locations at the corners of the copied surface are specified distances from the original surface.
        /// </summary>
        /// <param name="uMinvMin">Offset distance at Domain(0).Min, Domain(1).Min.</param>
        /// <param name="uMinvMax">Offset distance at Domain(0).Min, Domain(1).Max.</param>
        /// <param name="uMaxvMin">Offset distance at Domain(0).Max, Domain(1).Min.</param>
        /// <param name="uMaxvMax">Offset distance at Domain(0).Max, Domain(1).Max.</param>
        /// <param name="tolerance">The offset tolerance.</param>
        /// <returns>The offset surface if successful, null otherwise.</returns>
        public static Surface VariableOffset(this Surface surface, out Surface updatedInstance, double uMinvMin, double uMinvMax, double uMaxvMin, double uMaxvMax, double tolerance)
        {
            return ComputeServer.Post<Surface, Surface>(ApiAddress(), out updatedInstance, surface, uMinvMin, uMinvMax, uMaxvMin, uMaxvMax, tolerance);
        }

        /// <summary>
        /// Copies a surface so that all locations at the corners of the copied surface are specified distances from the original surface.
        /// </summary>
        /// <param name="uMinvMin">Offset distance at Domain(0).Min, Domain(1).Min.</param>
        /// <param name="uMinvMax">Offset distance at Domain(0).Min, Domain(1).Max.</param>
        /// <param name="uMaxvMin">Offset distance at Domain(0).Max, Domain(1).Min.</param>
        /// <param name="uMaxvMax">Offset distance at Domain(0).Max, Domain(1).Max.</param>
        /// <param name="tolerance">The offset tolerance.</param>
        /// <returns>The offset surface if successful, null otherwise.</returns>
        public static Surface VariableOffset(Remote<Surface> surface, out Surface updatedInstance, double uMinvMin, double uMinvMax, double uMaxvMin, double uMaxvMax, double tolerance)
        {
            return ComputeServer.Post<Surface, Surface>(ApiAddress(), out updatedInstance, surface, uMinvMin, uMinvMax, uMaxvMin, uMaxvMax, tolerance);
        }

        /// <summary>
        /// Copies a surface so that all locations at the corners, and from specified interior locations, of the copied surface are specified distances from the original surface.
        /// </summary>
        /// <param name="uMinvMin">Offset distance at Domain(0).Min, Domain(1).Min.</param>
        /// <param name="uMinvMax">Offset distance at Domain(0).Min, Domain(1).Max.</param>
        /// <param name="uMaxvMin">Offset distance at Domain(0).Max, Domain(1).Min.</param>
        /// <param name="uMaxvMax">Offset distance at Domain(0).Max, Domain(1).Max.</param>
        /// <param name="interiorParameters">An array of interior UV parameters to offset from.</param>
        /// <param name="interiorDistances">>An array of offset distances at the interior UV parameters.</param>
        /// <param name="tolerance">The offset tolerance.</param>
        /// <returns>The offset surface if successful, null otherwise.</returns>
        public static Surface VariableOffset(this Surface surface, out Surface updatedInstance, double uMinvMin, double uMinvMax, double uMaxvMin, double uMaxvMax, IEnumerable<Point2d> interiorParameters, IEnumerable<double> interiorDistances, double tolerance)
        {
            return ComputeServer.Post<Surface, Surface>(ApiAddress(), out updatedInstance, surface, uMinvMin, uMinvMax, uMaxvMin, uMaxvMax, interiorParameters, interiorDistances, tolerance);
        }

        /// <summary>
        /// Copies a surface so that all locations at the corners, and from specified interior locations, of the copied surface are specified distances from the original surface.
        /// </summary>
        /// <param name="uMinvMin">Offset distance at Domain(0).Min, Domain(1).Min.</param>
        /// <param name="uMinvMax">Offset distance at Domain(0).Min, Domain(1).Max.</param>
        /// <param name="uMaxvMin">Offset distance at Domain(0).Max, Domain(1).Min.</param>
        /// <param name="uMaxvMax">Offset distance at Domain(0).Max, Domain(1).Max.</param>
        /// <param name="interiorParameters">An array of interior UV parameters to offset from.</param>
        /// <param name="interiorDistances">>An array of offset distances at the interior UV parameters.</param>
        /// <param name="tolerance">The offset tolerance.</param>
        /// <returns>The offset surface if successful, null otherwise.</returns>
        public static Surface VariableOffset(Remote<Surface> surface, out Surface updatedInstance, double uMinvMin, double uMinvMax, double uMaxvMin, double uMaxvMax, IEnumerable<Point2d> interiorParameters, IEnumerable<double> interiorDistances, double tolerance)
        {
            return ComputeServer.Post<Surface, Surface>(ApiAddress(), out updatedInstance, surface, uMinvMin, uMinvMax, uMaxvMin, uMaxvMax, interiorParameters, interiorDistances, tolerance);
        }

        /// <summary>
        /// Gets an estimate of the size of the rectangle that would be created
        /// if the 3d surface where flattened into a rectangle.
        /// </summary>
        /// <param name="width">corresponds to the first surface parameter.</param>
        /// <param name="height">corresponds to the second surface parameter.</param>
        /// <returns>true if successful.</returns>
        /// <example>
        /// Re-parameterize a surface to minimize distortion in the map from parameter space to 3d.
        /// Surface surf = ...;
        /// double width, height;
        /// if ( surf.GetSurfaceSize( out width, out height ) )
        /// {
        ///   surf.SetDomain( 0, new ON_Interval( 0.0, width ) );
        ///   surf.SetDomain( 1, new ON_Interval( 0.0, height ) );
        /// }
        /// </example>
        public static bool GetSurfaceSize(this Surface surface, out double width, out double height)
        {
            return ComputeServer.Post<bool, double, double>(ApiAddress(), out width, out height, surface, );
        }

        /// <summary>
        /// Gets an estimate of the size of the rectangle that would be created
        /// if the 3d surface where flattened into a rectangle.
        /// </summary>
        /// <param name="width">corresponds to the first surface parameter.</param>
        /// <param name="height">corresponds to the second surface parameter.</param>
        /// <returns>true if successful.</returns>
        /// <example>
        /// Re-parameterize a surface to minimize distortion in the map from parameter space to 3d.
        /// Surface surf = ...;
        /// double width, height;
        /// if ( surf.GetSurfaceSize( out width, out height ) )
        /// {
        ///   surf.SetDomain( 0, new ON_Interval( 0.0, width ) );
        ///   surf.SetDomain( 1, new ON_Interval( 0.0, height ) );
        /// }
        /// </example>
        public static bool GetSurfaceSize(Remote<Surface> surface, out double width, out double height)
        {
            return ComputeServer.Post<bool, double, double>(ApiAddress(), out width, out height, surface, );
        }

        /// <summary>
        /// Gets the side that is closest, in terms of 3D-distance, to a U and V parameter.
        /// </summary>
        /// <param name="u">A u parameter.</param>
        /// <param name="v">A v parameter.</param>
        /// <returns>A side.</returns>
        public static IsoStatus ClosestSide(this Surface surface, double u, double v)
        {
            return ComputeServer.Post<IsoStatus>(ApiAddress(), surface, u, v);
        }

        /// <summary>
        /// Gets the side that is closest, in terms of 3D-distance, to a U and V parameter.
        /// </summary>
        /// <param name="u">A u parameter.</param>
        /// <param name="v">A v parameter.</param>
        /// <returns>A side.</returns>
        public static IsoStatus ClosestSide(Remote<Surface> surface, double u, double v)
        {
            return ComputeServer.Post<IsoStatus>(ApiAddress(), surface, u, v);
        }

        /// <summary>
        /// Extends an untrimmed surface along one edge.
        /// </summary>
        /// <param name="edge">
        /// Edge to extend.  Must be North, South, East, or West.
        /// </param>
        /// <param name="extensionLength">distance to extend.</param>
        /// <param name="smooth">
        /// true for smooth (C-infinity) extension. 
        /// false for a C1- ruled extension.
        /// </param>
        /// <returns>New extended surface on success.</returns>
        public static Surface Extend(this Surface surface, IsoStatus edge, double extensionLength, bool smooth)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), surface, edge, extensionLength, smooth);
        }

        /// <summary>
        /// Extends an untrimmed surface along one edge.
        /// </summary>
        /// <param name="edge">
        /// Edge to extend.  Must be North, South, East, or West.
        /// </param>
        /// <param name="extensionLength">distance to extend.</param>
        /// <param name="smooth">
        /// true for smooth (C-infinity) extension. 
        /// false for a C1- ruled extension.
        /// </param>
        /// <returns>New extended surface on success.</returns>
        public static Surface Extend(Remote<Surface> surface, IsoStatus edge, double extensionLength, bool smooth)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), surface, edge, extensionLength, smooth);
        }

        /// <summary>
        /// Rebuilds an existing surface to a given degree and point count.
        /// </summary>
        /// <param name="uDegree">the output surface u degree.</param>
        /// <param name="vDegree">the output surface u degree.</param>
        /// <param name="uPointCount">
        /// The number of points in the output surface u direction. Must be bigger
        /// than uDegree (maximum value is 1000)
        /// </param>
        /// <param name="vPointCount">
        /// The number of points in the output surface v direction. Must be bigger
        /// than vDegree (maximum value is 1000)
        /// </param>
        /// <returns>new rebuilt surface on success. null on failure.</returns>
        public static NurbsSurface Rebuild(this Surface surface, int uDegree, int vDegree, int uPointCount, int vPointCount)
        {
            return ComputeServer.Post<NurbsSurface>(ApiAddress(), surface, uDegree, vDegree, uPointCount, vPointCount);
        }

        /// <summary>
        /// Rebuilds an existing surface to a given degree and point count.
        /// </summary>
        /// <param name="uDegree">the output surface u degree.</param>
        /// <param name="vDegree">the output surface u degree.</param>
        /// <param name="uPointCount">
        /// The number of points in the output surface u direction. Must be bigger
        /// than uDegree (maximum value is 1000)
        /// </param>
        /// <param name="vPointCount">
        /// The number of points in the output surface v direction. Must be bigger
        /// than vDegree (maximum value is 1000)
        /// </param>
        /// <returns>new rebuilt surface on success. null on failure.</returns>
        public static NurbsSurface Rebuild(Remote<Surface> surface, int uDegree, int vDegree, int uPointCount, int vPointCount)
        {
            return ComputeServer.Post<NurbsSurface>(ApiAddress(), surface, uDegree, vDegree, uPointCount, vPointCount);
        }

        /// <summary>
        /// Rebuilds an existing surface with a new surface to a given point count in either the u or v directions independently.
        /// </summary>
        /// <param name="direction">The direction (0 = U, 1 = V).</param>
        /// <param name="pointCount">The number of points in the output surface in the "direction" direction.</param>
        /// <param name="loftType">The loft type</param>
        /// <param name="refitTolerance">The refit tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>new rebuilt surface on success. null on failure.</returns>
        public static NurbsSurface RebuildOneDirection(this Surface surface, int direction, int pointCount, LoftType loftType, double refitTolerance)
        {
            return ComputeServer.Post<NurbsSurface>(ApiAddress(), surface, direction, pointCount, loftType, refitTolerance);
        }

        /// <summary>
        /// Rebuilds an existing surface with a new surface to a given point count in either the u or v directions independently.
        /// </summary>
        /// <param name="direction">The direction (0 = U, 1 = V).</param>
        /// <param name="pointCount">The number of points in the output surface in the "direction" direction.</param>
        /// <param name="loftType">The loft type</param>
        /// <param name="refitTolerance">The refit tolerance. When in doubt, use the document's model absolute tolerance.</param>
        /// <returns>new rebuilt surface on success. null on failure.</returns>
        public static NurbsSurface RebuildOneDirection(Remote<Surface> surface, int direction, int pointCount, LoftType loftType, double refitTolerance)
        {
            return ComputeServer.Post<NurbsSurface>(ApiAddress(), surface, direction, pointCount, loftType, refitTolerance);
        }

        /// <summary>
        /// Input the parameters of the point on the surface that is closest to testPoint.
        /// </summary>
        /// <param name="testPoint">A point to test against.</param>
        /// <param name="u">U parameter of the surface that is closest to testPoint.</param>
        /// <param name="v">V parameter of the surface that is closest to testPoint.</param>
        /// <returns>true on success, false on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_orientonsrf.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_orientonsrf.cs' lang='cs'/>
        /// <code source='examples\py\ex_orientonsrf.py' lang='py'/>
        /// </example>
        public static bool ClosestPoint(this Surface surface, Point3d testPoint, out double u, out double v)
        {
            return ComputeServer.Post<bool, double, double>(ApiAddress(), out u, out v, surface, testPoint);
        }

        /// <summary>
        /// Input the parameters of the point on the surface that is closest to testPoint.
        /// </summary>
        /// <param name="testPoint">A point to test against.</param>
        /// <param name="u">U parameter of the surface that is closest to testPoint.</param>
        /// <param name="v">V parameter of the surface that is closest to testPoint.</param>
        /// <returns>true on success, false on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_orientonsrf.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_orientonsrf.cs' lang='cs'/>
        /// <code source='examples\py\ex_orientonsrf.py' lang='py'/>
        /// </example>
        public static bool ClosestPoint(Remote<Surface> surface, Point3d testPoint, out double u, out double v)
        {
            return ComputeServer.Post<bool, double, double>(ApiAddress(), out u, out v, surface, testPoint);
        }

        /// <summary>
        /// Find parameters of the point on a surface that is locally closest to
        /// the testPoint. The search for a local close point starts at seed parameters.
        /// </summary>
        /// <param name="testPoint">A point to test against.</param>
        /// <param name="seedU">The seed parameter in the U direction.</param>
        /// <param name="seedV">The seed parameter in the V direction.</param>
        /// <param name="u">U parameter of the surface that is closest to testPoint.</param>
        /// <param name="v">V parameter of the surface that is closest to testPoint.</param>
        /// <returns>true if the search is successful, false if the search fails.</returns>
        public static bool LocalClosestPoint(this Surface surface, Point3d testPoint, double seedU, double seedV, out double u, out double v)
        {
            return ComputeServer.Post<bool, double, double>(ApiAddress(), out u, out v, surface, testPoint, seedU, seedV);
        }

        /// <summary>
        /// Find parameters of the point on a surface that is locally closest to
        /// the testPoint. The search for a local close point starts at seed parameters.
        /// </summary>
        /// <param name="testPoint">A point to test against.</param>
        /// <param name="seedU">The seed parameter in the U direction.</param>
        /// <param name="seedV">The seed parameter in the V direction.</param>
        /// <param name="u">U parameter of the surface that is closest to testPoint.</param>
        /// <param name="v">V parameter of the surface that is closest to testPoint.</param>
        /// <returns>true if the search is successful, false if the search fails.</returns>
        public static bool LocalClosestPoint(Remote<Surface> surface, Point3d testPoint, double seedU, double seedV, out double u, out double v)
        {
            return ComputeServer.Post<bool, double, double>(ApiAddress(), out u, out v, surface, testPoint, seedU, seedV);
        }

        /// <summary>
        /// Constructs a new surface which is offset from the current surface.
        /// </summary>
        /// <param name="distance">Distance (along surface normal) to offset.</param>
        /// <param name="tolerance">Offset accuracy.</param>
        /// <returns>The offset surface or null on failure.</returns>
        public static Surface Offset(this Surface surface, double distance, double tolerance)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), surface, distance, tolerance);
        }

        /// <summary>
        /// Constructs a new surface which is offset from the current surface.
        /// </summary>
        /// <param name="distance">Distance (along surface normal) to offset.</param>
        /// <param name="tolerance">Offset accuracy.</param>
        /// <returns>The offset surface or null on failure.</returns>
        public static Surface Offset(Remote<Surface> surface, double distance, double tolerance)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), surface, distance, tolerance);
        }

        /// <summary>Fits a new surface through an existing surface.</summary>
        /// <param name="uDegree">the output surface U degree. Must be bigger than 1.</param>
        /// <param name="vDegree">the output surface V degree. Must be bigger than 1.</param>
        /// <param name="fitTolerance">The fitting tolerance.</param>
        /// <returns>A surface, or null on error.</returns>
        public static Surface Fit(this Surface surface, int uDegree, int vDegree, double fitTolerance)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), surface, uDegree, vDegree, fitTolerance);
        }

        /// <summary>Fits a new surface through an existing surface.</summary>
        /// <param name="uDegree">the output surface U degree. Must be bigger than 1.</param>
        /// <param name="vDegree">the output surface V degree. Must be bigger than 1.</param>
        /// <param name="fitTolerance">The fitting tolerance.</param>
        /// <returns>A surface, or null on error.</returns>
        public static Surface Fit(Remote<Surface> surface, int uDegree, int vDegree, double fitTolerance)
        {
            return ComputeServer.Post<Surface>(ApiAddress(), surface, uDegree, vDegree, fitTolerance);
        }

        /// <summary>
        /// Returns a curve that interpolates points on a surface. The interpolant lies on the surface.
        /// </summary>
        /// <param name="points">List of at least two UV parameter locations on the surface.</param>
        /// <param name="tolerance">Tolerance used for the fit of the push-up curve. Generally, the resulting interpolating curve will be within tolerance of the surface.</param>
        /// <returns>A new NURBS curve if successful, or null on error.</returns>
        public static NurbsCurve InterpolatedCurveOnSurfaceUV(this Surface surface, System.Collections.Generic.IEnumerable<Point2d> points, double tolerance)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), surface, points, tolerance);
        }

        /// <summary>
        /// Returns a curve that interpolates points on a surface. The interpolant lies on the surface.
        /// </summary>
        /// <param name="points">List of at least two UV parameter locations on the surface.</param>
        /// <param name="tolerance">Tolerance used for the fit of the push-up curve. Generally, the resulting interpolating curve will be within tolerance of the surface.</param>
        /// <returns>A new NURBS curve if successful, or null on error.</returns>
        public static NurbsCurve InterpolatedCurveOnSurfaceUV(Remote<Surface> surface, System.Collections.Generic.IEnumerable<Point2d> points, double tolerance)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), surface, points, tolerance);
        }

        /// <summary>
        /// Returns a curve that interpolates points on a surface. The interpolant lies on the surface.
        /// </summary>
        /// <param name="points">List of at least two UV parameter locations on the surface.</param>
        /// <param name="tolerance">Tolerance used for the fit of the push-up curve. Generally, the resulting interpolating curve will be within tolerance of the surface.</param>
        /// <param name="closed">If false, the interpolating curve is not closed. If true, the interpolating curve is closed, and the last point and first point should generally not be equal.</param>
        /// <param name="closedSurfaceHandling">
        /// If 0, all points must be in the rectangular domain of the surface. If the surface is closed in some direction, 
        /// then this routine will interpret each point and place it at an appropriate location in the covering space. 
        /// This is the simplest option and should give good results. 
        /// If 1, then more options for more control of handling curves going across seams are available.
        /// If the surface is closed in some direction, then the points are taken as points in the covering space. 
        /// Example, if srf.IsClosed(0)=true and srf.IsClosed(1)=false and srf.Domain(0)=srf.Domain(1)=Interval(0,1) 
        /// then if closedSurfaceHandling=1 a point(u, v) in points can have any value for the u coordinate, but must have 0&lt;=v&lt;=1.  
        /// In particular, if points = { (0.0,0.5), (2.0,0.5) } then the interpolating curve will wrap around the surface two times in the closed direction before ending at start of the curve.
        /// If closed=true the last point should equal the first point plus an integer multiple of the period on a closed direction.
        /// </param>
        /// <returns>A new NURBS curve if successful, or null on error.</returns>
        public static NurbsCurve InterpolatedCurveOnSurfaceUV(this Surface surface, System.Collections.Generic.IEnumerable<Point2d> points, double tolerance, bool closed, int closedSurfaceHandling)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), surface, points, tolerance, closed, closedSurfaceHandling);
        }

        /// <summary>
        /// Returns a curve that interpolates points on a surface. The interpolant lies on the surface.
        /// </summary>
        /// <param name="points">List of at least two UV parameter locations on the surface.</param>
        /// <param name="tolerance">Tolerance used for the fit of the push-up curve. Generally, the resulting interpolating curve will be within tolerance of the surface.</param>
        /// <param name="closed">If false, the interpolating curve is not closed. If true, the interpolating curve is closed, and the last point and first point should generally not be equal.</param>
        /// <param name="closedSurfaceHandling">
        /// If 0, all points must be in the rectangular domain of the surface. If the surface is closed in some direction, 
        /// then this routine will interpret each point and place it at an appropriate location in the covering space. 
        /// This is the simplest option and should give good results. 
        /// If 1, then more options for more control of handling curves going across seams are available.
        /// If the surface is closed in some direction, then the points are taken as points in the covering space. 
        /// Example, if srf.IsClosed(0)=true and srf.IsClosed(1)=false and srf.Domain(0)=srf.Domain(1)=Interval(0,1) 
        /// then if closedSurfaceHandling=1 a point(u, v) in points can have any value for the u coordinate, but must have 0&lt;=v&lt;=1.  
        /// In particular, if points = { (0.0,0.5), (2.0,0.5) } then the interpolating curve will wrap around the surface two times in the closed direction before ending at start of the curve.
        /// If closed=true the last point should equal the first point plus an integer multiple of the period on a closed direction.
        /// </param>
        /// <returns>A new NURBS curve if successful, or null on error.</returns>
        public static NurbsCurve InterpolatedCurveOnSurfaceUV(Remote<Surface> surface, System.Collections.Generic.IEnumerable<Point2d> points, double tolerance, bool closed, int closedSurfaceHandling)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), surface, points, tolerance, closed, closedSurfaceHandling);
        }

        /// <summary>
        /// Constructs an interpolated curve on a surface, using 3D points.
        /// </summary>
        /// <param name="points">A list, an array or any enumerable set of points.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>A new NURBS curve, or null on error.</returns>
        public static NurbsCurve InterpolatedCurveOnSurface(this Surface surface, System.Collections.Generic.IEnumerable<Point3d> points, double tolerance)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), surface, points, tolerance);
        }

        /// <summary>
        /// Constructs an interpolated curve on a surface, using 3D points.
        /// </summary>
        /// <param name="points">A list, an array or any enumerable set of points.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>A new NURBS curve, or null on error.</returns>
        public static NurbsCurve InterpolatedCurveOnSurface(Remote<Surface> surface, System.Collections.Generic.IEnumerable<Point3d> points, double tolerance)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), surface, points, tolerance);
        }

        /// <summary>
        /// Constructs a geodesic between 2 points, used by ShortPath command in Rhino.
        /// </summary>
        /// <param name="start">start point of curve in parameter space. Points must be distinct in the domain of the surface.</param>
        /// <param name="end">end point of curve in parameter space. Points must be distinct in the domain of the surface.</param>
        /// <param name="tolerance">tolerance used in fitting discrete solution.</param>
        /// <returns>a geodesic curve on the surface on success. null on failure.</returns>
        public static Curve ShortPath(this Surface surface, Point2d start, Point2d end, double tolerance)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), surface, start, end, tolerance);
        }

        /// <summary>
        /// Constructs a geodesic between 2 points, used by ShortPath command in Rhino.
        /// </summary>
        /// <param name="start">start point of curve in parameter space. Points must be distinct in the domain of the surface.</param>
        /// <param name="end">end point of curve in parameter space. Points must be distinct in the domain of the surface.</param>
        /// <param name="tolerance">tolerance used in fitting discrete solution.</param>
        /// <returns>a geodesic curve on the surface on success. null on failure.</returns>
        public static Curve ShortPath(Remote<Surface> surface, Point2d start, Point2d end, double tolerance)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), surface, start, end, tolerance);
        }

        /// <summary>
        /// Computes a 3d curve that is the composite of a 2d curve and the surface map.
        /// </summary>
        /// <param name="curve2d">a 2d curve whose image is in the surface's domain.</param>
        /// <param name="tolerance">
        /// the maximum acceptable distance from the returned 3d curve to the image of curve_2d on the surface.
        /// </param>
        /// <param name="curve2dSubdomain">The curve interval (a sub-domain of the original curve) to use.</param>
        /// <returns>3d curve.</returns>
        public static Curve Pushup(this Surface surface, Curve curve2d, double tolerance, Interval curve2dSubdomain)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), surface, curve2d, tolerance, curve2dSubdomain);
        }

        /// <summary>
        /// Computes a 3d curve that is the composite of a 2d curve and the surface map.
        /// </summary>
        /// <param name="curve2d">a 2d curve whose image is in the surface's domain.</param>
        /// <param name="tolerance">
        /// the maximum acceptable distance from the returned 3d curve to the image of curve_2d on the surface.
        /// </param>
        /// <param name="curve2dSubdomain">The curve interval (a sub-domain of the original curve) to use.</param>
        /// <returns>3d curve.</returns>
        public static Curve Pushup(Remote<Surface> surface, Remote<Curve> curve2d, double tolerance, Interval curve2dSubdomain)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), surface, curve2d, tolerance, curve2dSubdomain);
        }

        /// <summary>
        /// Computes a 3d curve that is the composite of a 2d curve and the surface map.
        /// </summary>
        /// <param name="curve2d">a 2d curve whose image is in the surface's domain.</param>
        /// <param name="tolerance">
        /// the maximum acceptable distance from the returned 3d curve to the image of curve_2d on the surface.
        /// </param>
        /// <returns>3d curve.</returns>
        public static Curve Pushup(this Surface surface, Curve curve2d, double tolerance)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), surface, curve2d, tolerance);
        }

        /// <summary>
        /// Computes a 3d curve that is the composite of a 2d curve and the surface map.
        /// </summary>
        /// <param name="curve2d">a 2d curve whose image is in the surface's domain.</param>
        /// <param name="tolerance">
        /// the maximum acceptable distance from the returned 3d curve to the image of curve_2d on the surface.
        /// </param>
        /// <returns>3d curve.</returns>
        public static Curve Pushup(Remote<Surface> surface, Remote<Curve> curve2d, double tolerance)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), surface, curve2d, tolerance);
        }

        /// <summary>
        /// Pulls a 3d curve back to the surface's parameter space.
        /// </summary>
        /// <param name="curve3d">The curve to pull.</param>
        /// <param name="tolerance">
        /// the maximum acceptable 3d distance between from surface(curve_2d(t))
        /// to the locus of points on the surface that are closest to curve_3d.
        /// </param>
        /// <returns>2d curve.</returns>
        public static Curve Pullback(this Surface surface, Curve curve3d, double tolerance)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), surface, curve3d, tolerance);
        }

        /// <summary>
        /// Pulls a 3d curve back to the surface's parameter space.
        /// </summary>
        /// <param name="curve3d">The curve to pull.</param>
        /// <param name="tolerance">
        /// the maximum acceptable 3d distance between from surface(curve_2d(t))
        /// to the locus of points on the surface that are closest to curve_3d.
        /// </param>
        /// <returns>2d curve.</returns>
        public static Curve Pullback(Remote<Surface> surface, Remote<Curve> curve3d, double tolerance)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), surface, curve3d, tolerance);
        }

        /// <summary>
        /// Pulls a 3d curve back to the surface's parameter space.
        /// </summary>
        /// <param name="curve3d">A curve.</param>
        /// <param name="tolerance">
        /// the maximum acceptable 3d distance between from surface(curve_2d(t))
        /// to the locus of points on the surface that are closest to curve_3d.
        /// </param>
        /// <param name="curve3dSubdomain">A sub-domain of the curve to sample.</param>
        /// <returns>2d curve.</returns>
        public static Curve Pullback(this Surface surface, Curve curve3d, double tolerance, Interval curve3dSubdomain)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), surface, curve3d, tolerance, curve3dSubdomain);
        }

        /// <summary>
        /// Pulls a 3d curve back to the surface's parameter space.
        /// </summary>
        /// <param name="curve3d">A curve.</param>
        /// <param name="tolerance">
        /// the maximum acceptable 3d distance between from surface(curve_2d(t))
        /// to the locus of points on the surface that are closest to curve_3d.
        /// </param>
        /// <param name="curve3dSubdomain">A sub-domain of the curve to sample.</param>
        /// <returns>2d curve.</returns>
        public static Curve Pullback(Remote<Surface> surface, Remote<Curve> curve3d, double tolerance, Interval curve3dSubdomain)
        {
            return ComputeServer.Post<Curve>(ApiAddress(), surface, curve3d, tolerance, curve3dSubdomain);
        }
    }

    public static class SubDCompute
    {
        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return ComputeServer.ApiAddress(typeof(SubD), caller);
        }

        /// <summary>
        /// Create a Brep based on this SubD geometry.
        /// </summary>
        /// <param name="options">
        /// The SubD to Brep conversion options. Use SubDToBrepOptions.Default 
        /// for sensible defaults. Currently, these return unpacked faces 
        /// and locally-G1 vertices in the output Brep.
        /// </param>
        /// <returns>A new Brep if successful, or null on failure.</returns>
        public static Brep ToBrep(this SubD subd, out SubD updatedInstance, SubDToBrepOptions options)
        {
            return ComputeServer.Post<Brep, SubD>(ApiAddress(), out updatedInstance, subd, options);
        }

        /// <summary>
        /// Create a new SubD from a mesh.
        /// </summary>
        /// <param name="mesh">The input mesh.</param>
        /// <returns>A new SubD if successful, or null on failure.</returns>
        public static SubD CreateFromMesh(Mesh mesh)
        {
            return ComputeServer.Post<SubD>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Create a new SubD from a mesh.
        /// </summary>
        /// <param name="mesh">The input mesh.</param>
        /// <returns>A new SubD if successful, or null on failure.</returns>
        public static SubD CreateFromMesh(Remote<Mesh> mesh)
        {
            return ComputeServer.Post<SubD>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Create a new SubD from a mesh.
        /// </summary>
        /// <param name="mesh">The input mesh.</param>
        /// <param name="options">The SubD creation options.</param>
        /// <returns>A new SubD if successful, or null on failure.</returns>
        public static SubD CreateFromMesh(Mesh mesh, SubDCreationOptions options)
        {
            return ComputeServer.Post<SubD>(ApiAddress(), mesh, options);
        }

        /// <summary>
        /// Create a new SubD from a mesh.
        /// </summary>
        /// <param name="mesh">The input mesh.</param>
        /// <param name="options">The SubD creation options.</param>
        /// <returns>A new SubD if successful, or null on failure.</returns>
        public static SubD CreateFromMesh(Remote<Mesh> mesh, SubDCreationOptions options)
        {
            return ComputeServer.Post<SubD>(ApiAddress(), mesh, options);
        }

        /// <summary>
        /// Makes a new SubD with vertices offset at distance in the direction of the control net vertex normals.
        /// Optionally, based on the value of solidify, adds the input SubD and a ribbon of faces along any naked edges.
        /// </summary>
        /// <param name="distance">The distance to offset.</param>
        /// <param name="solidify">true if the output SubD should be turned into a closed SubD.</param>
        /// <returns>A new SubD if successful, or null on failure.</returns>
        public static SubD Offset(this SubD subd, double distance, bool solidify)
        {
            return ComputeServer.Post<SubD>(ApiAddress(), subd, distance, solidify);
        }

        /// <summary>
        /// Creates a SubD lofted through shape curves.
        /// </summary>
        /// <param name="curves">An enumeration of SubD-friendly NURBS curves to loft through.</param>
        /// <param name="closed">Creates a SubD that is closed in the lofting direction. Must have three or more shape curves.</param>
        /// <param name="addCorners">With open curves, adds creased vertices to the SubD at both ends of the first and last curves.</param>
        /// <param name="addCreases">With kinked curves, adds creased edges to the SubD along the kinks.</param>
        /// <param name="divisions">The segment number between adjacent input curves.</param>
        /// <returns>A new SubD if successful, or null on failure.</returns>
        /// <remarks>
        /// Shape curves must be in the proper order and orientation and have point counts for the desired surface.
        /// Shape curves must be either all open or all closed.
        /// </remarks>
        public static SubD CreateFromLoft(IEnumerable<NurbsCurve> curves, bool closed, bool addCorners, bool addCreases, int divisions)
        {
            return ComputeServer.Post<SubD>(ApiAddress(), curves, closed, addCorners, addCreases, divisions);
        }

        /// <summary>
        /// Creates a SubD lofted through shape curves.
        /// </summary>
        /// <param name="curves">An enumeration of SubD-friendly NURBS curves to loft through.</param>
        /// <param name="closed">Creates a SubD that is closed in the lofting direction. Must have three or more shape curves.</param>
        /// <param name="addCorners">With open curves, adds creased vertices to the SubD at both ends of the first and last curves.</param>
        /// <param name="addCreases">With kinked curves, adds creased edges to the SubD along the kinks.</param>
        /// <param name="divisions">The segment number between adjacent input curves.</param>
        /// <returns>A new SubD if successful, or null on failure.</returns>
        /// <remarks>
        /// Shape curves must be in the proper order and orientation and have point counts for the desired surface.
        /// Shape curves must be either all open or all closed.
        /// </remarks>
        public static SubD CreateFromLoft(Remote<IEnumerable<NurbsCurve>> curves, bool closed, bool addCorners, bool addCreases, int divisions)
        {
            return ComputeServer.Post<SubD>(ApiAddress(), curves, closed, addCorners, addCreases, divisions);
        }

        /// <summary>
        /// Fits a SubD through a series of profile curves that define the SubD cross-sections and one curve that defines a SubD edge.
        /// </summary>
        /// <param name="rail1">A SubD-friendly NURBS curve to sweep along.</param>
        /// <param name="shapes">An enumeration of SubD-friendly NURBS curves to sweep through.</param>
        /// <param name="closed">Creates a SubD that is closed in the rail curve direction.</param>
        /// <param name="addCorners">With open curves, adds creased vertices to the SubD at both ends of the first and last curves.</param>
        /// <param name="roadlikeFrame">
        /// Determines how sweep frame rotations are calculated.
        /// If false (Freeform), frame are propogated based on a refrence direction taken from the rail curve curvature direction.
        /// If true (Roadlike), frame rotations are calculated based on a vector supplied in "roadlikeNormal" and the world coordinate system.
        /// </param>
        /// <param name="roadlikeNormal">
        /// If roadlikeFrame = true, provide 3D vector used to calculate the frame rotations for sweep shapes.
        /// If roadlikeFrame = false, then pass <see cref=" Vector3d.Unset"/>.
        /// </param>
        /// <returns>A new SubD if successful, or null on failure.</returns>
        /// <remarks>
        /// Shape curves must be in the proper order and orientation.
        /// Shape curves must have the same point counts and rail curves must have the same point counts.
        /// Shape curves will relocated to the nearest pair of Greville points on the rails.
        /// Shape curves will be made at each pair of rail edit points where there isn't an input shape.
        /// </remarks>
        public static SubD CreateFromSweep(NurbsCurve rail1, IEnumerable<NurbsCurve> shapes, bool closed, bool addCorners, bool roadlikeFrame, Vector3d roadlikeNormal)
        {
            return ComputeServer.Post<SubD>(ApiAddress(), rail1, shapes, closed, addCorners, roadlikeFrame, roadlikeNormal);
        }

        /// <summary>
        /// Fits a SubD through a series of profile curves that define the SubD cross-sections and one curve that defines a SubD edge.
        /// </summary>
        /// <param name="rail1">A SubD-friendly NURBS curve to sweep along.</param>
        /// <param name="shapes">An enumeration of SubD-friendly NURBS curves to sweep through.</param>
        /// <param name="closed">Creates a SubD that is closed in the rail curve direction.</param>
        /// <param name="addCorners">With open curves, adds creased vertices to the SubD at both ends of the first and last curves.</param>
        /// <param name="roadlikeFrame">
        /// Determines how sweep frame rotations are calculated.
        /// If false (Freeform), frame are propogated based on a refrence direction taken from the rail curve curvature direction.
        /// If true (Roadlike), frame rotations are calculated based on a vector supplied in "roadlikeNormal" and the world coordinate system.
        /// </param>
        /// <param name="roadlikeNormal">
        /// If roadlikeFrame = true, provide 3D vector used to calculate the frame rotations for sweep shapes.
        /// If roadlikeFrame = false, then pass <see cref=" Vector3d.Unset"/>.
        /// </param>
        /// <returns>A new SubD if successful, or null on failure.</returns>
        /// <remarks>
        /// Shape curves must be in the proper order and orientation.
        /// Shape curves must have the same point counts and rail curves must have the same point counts.
        /// Shape curves will relocated to the nearest pair of Greville points on the rails.
        /// Shape curves will be made at each pair of rail edit points where there isn't an input shape.
        /// </remarks>
        public static SubD CreateFromSweep(Remote<NurbsCurve> rail1, Remote<IEnumerable<NurbsCurve>> shapes, bool closed, bool addCorners, bool roadlikeFrame, Vector3d roadlikeNormal)
        {
            return ComputeServer.Post<SubD>(ApiAddress(), rail1, shapes, closed, addCorners, roadlikeFrame, roadlikeNormal);
        }

        /// <summary>
        /// Fits a SubD through a series of profile curves that define the SubD cross-sections and two curves that defines SubD edges.
        /// </summary>
        /// <param name="rail1">The first SubD-friendly NURBS curve to sweep along.</param>
        /// <param name="rail2">The second SubD-friendly NURBS curve to sweep along.</param>
        /// <param name="shapes">An enumeration of SubD-friendly NURBS curves to sweep through.</param>
        /// <param name="closed">Creates a SubD that is closed in the rail curve direction.</param>
        /// <param name="addCorners">With open curves, adds creased vertices to the SubD at both ends of the first and last curves.</param>
        /// <returns>A new SubD if successful, or null on failure.</returns>
        /// <remarks>
        /// Shape curves must be in the proper order and orientation.
        /// Shape curves must have the same point counts and rail curves must have the same point counts.
        /// Shape curves will relocated to the nearest pair of Greville points on the rails.
        /// Shape curves will be made at each pair of rail edit points where there isn't an input shape.
        /// </remarks>
        public static SubD CreateFromSweep(NurbsCurve rail1, NurbsCurve rail2, IEnumerable<NurbsCurve> shapes, bool closed, bool addCorners)
        {
            return ComputeServer.Post<SubD>(ApiAddress(), rail1, rail2, shapes, closed, addCorners);
        }

        /// <summary>
        /// Fits a SubD through a series of profile curves that define the SubD cross-sections and two curves that defines SubD edges.
        /// </summary>
        /// <param name="rail1">The first SubD-friendly NURBS curve to sweep along.</param>
        /// <param name="rail2">The second SubD-friendly NURBS curve to sweep along.</param>
        /// <param name="shapes">An enumeration of SubD-friendly NURBS curves to sweep through.</param>
        /// <param name="closed">Creates a SubD that is closed in the rail curve direction.</param>
        /// <param name="addCorners">With open curves, adds creased vertices to the SubD at both ends of the first and last curves.</param>
        /// <returns>A new SubD if successful, or null on failure.</returns>
        /// <remarks>
        /// Shape curves must be in the proper order and orientation.
        /// Shape curves must have the same point counts and rail curves must have the same point counts.
        /// Shape curves will relocated to the nearest pair of Greville points on the rails.
        /// Shape curves will be made at each pair of rail edit points where there isn't an input shape.
        /// </remarks>
        public static SubD CreateFromSweep(Remote<NurbsCurve> rail1, Remote<NurbsCurve> rail2, Remote<IEnumerable<NurbsCurve>> shapes, bool closed, bool addCorners)
        {
            return ComputeServer.Post<SubD>(ApiAddress(), rail1, rail2, shapes, closed, addCorners);
        }

        /// <summary>
        /// Modifies the SubD so that the SubD vertex limit surface points are
        /// equal to surface_points[]
        /// </summary>
        /// <param name="surfacePoints">
        /// point for limit surface to interpolate. surface_points[i] is the
        /// location for the i-th vertex returned by SubVertexIterator vit(this)
        /// </param>
        /// <returns></returns>
        public static bool InterpolateSurfacePoints(this SubD subd, out SubD updatedInstance, Point3d[] surfacePoints)
        {
            return ComputeServer.Post<bool, SubD>(ApiAddress(), out updatedInstance, subd, surfacePoints);
        }
    }

    public static class GeometryBaseCompute
    {
        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return ComputeServer.ApiAddress(typeof(GeometryBase), caller);
        }

        /// <summary>
        /// Bounding box solver. Gets the world axis aligned bounding box for the geometry.
        /// </summary>
        /// <param name="accurate">If true, a physically accurate bounding box will be computed. 
        /// If not, a bounding box estimate will be computed. For some geometry types there is no 
        /// difference between the estimate and the accurate bounding box. Estimated bounding boxes 
        /// can be computed much (much) faster than accurate (or "tight") bounding boxes. 
        /// Estimated bounding boxes are always similar to or larger than accurate bounding boxes.</param>
        /// <returns>
        /// The bounding box of the geometry in world coordinates or BoundingBox.Empty 
        /// if not bounding box could be found.
        /// </returns>
        /// <example>
        /// <code source='examples\vbnet\ex_curveboundingbox.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_curveboundingbox.cs' lang='cs'/>
        /// <code source='examples\py\ex_curveboundingbox.py' lang='py'/>
        /// </example>
        public static BoundingBox GetBoundingBox(this GeometryBase geometrybase, bool accurate)
        {
            return ComputeServer.Post<BoundingBox>(ApiAddress(), geometrybase, accurate);
        }

        /// <summary>
        /// Bounding box solver. Gets the world axis aligned bounding box for the geometry.
        /// </summary>
        /// <param name="accurate">If true, a physically accurate bounding box will be computed. 
        /// If not, a bounding box estimate will be computed. For some geometry types there is no 
        /// difference between the estimate and the accurate bounding box. Estimated bounding boxes 
        /// can be computed much (much) faster than accurate (or "tight") bounding boxes. 
        /// Estimated bounding boxes are always similar to or larger than accurate bounding boxes.</param>
        /// <returns>
        /// The bounding box of the geometry in world coordinates or BoundingBox.Empty 
        /// if not bounding box could be found.
        /// </returns>
        /// <example>
        /// <code source='examples\vbnet\ex_curveboundingbox.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_curveboundingbox.cs' lang='cs'/>
        /// <code source='examples\py\ex_curveboundingbox.py' lang='py'/>
        /// </example>
        public static BoundingBox GetBoundingBox(Remote<GeometryBase> geometrybase, bool accurate)
        {
            return ComputeServer.Post<BoundingBox>(ApiAddress(), geometrybase, accurate);
        }

        /// <summary>
        /// Aligned Bounding box solver. Gets the world axis aligned bounding box for the transformed geometry.
        /// </summary>
        /// <param name="xform">Transformation to apply to object prior to the BoundingBox computation. 
        /// The geometry itself is not modified.</param>
        /// <returns>The accurate bounding box of the transformed geometry in world coordinates 
        /// or BoundingBox.Empty if not bounding box could be found.</returns>
        public static BoundingBox GetBoundingBox(this GeometryBase geometrybase, Transform xform)
        {
            return ComputeServer.Post<BoundingBox>(ApiAddress(), geometrybase, xform);
        }

        /// <summary>
        /// Aligned Bounding box solver. Gets the world axis aligned bounding box for the transformed geometry.
        /// </summary>
        /// <param name="xform">Transformation to apply to object prior to the BoundingBox computation. 
        /// The geometry itself is not modified.</param>
        /// <returns>The accurate bounding box of the transformed geometry in world coordinates 
        /// or BoundingBox.Empty if not bounding box could be found.</returns>
        public static BoundingBox GetBoundingBox(Remote<GeometryBase> geometrybase, Transform xform)
        {
            return ComputeServer.Post<BoundingBox>(ApiAddress(), geometrybase, xform);
        }

        /// <summary>
        /// Determines if two geometries equal one another, in pure geometrical shape.
        /// This version only compares the geometry itself and does not include any user
        /// data comparisons.
        /// This is a comparison by value: for two identical items it will be true, no matter
        /// where in memory they may be stored.
        /// </summary>
        /// <param name="first">The first geometry</param>
        /// <param name="second">The second geometry</param>
        /// <returns>The indication of equality</returns>
        public static bool GeometryEquals(GeometryBase first, GeometryBase second)
        {
            return ComputeServer.Post<bool>(ApiAddress(), first, second);
        }

        /// <summary>
        /// Determines if two geometries equal one another, in pure geometrical shape.
        /// This version only compares the geometry itself and does not include any user
        /// data comparisons.
        /// This is a comparison by value: for two identical items it will be true, no matter
        /// where in memory they may be stored.
        /// </summary>
        /// <param name="first">The first geometry</param>
        /// <param name="second">The second geometry</param>
        /// <returns>The indication of equality</returns>
        public static bool GeometryEquals(Remote<GeometryBase> first, Remote<GeometryBase> second)
        {
            return ComputeServer.Post<bool>(ApiAddress(), first, second);
        }
    }

    public static class MeshCompute
    {
        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return ComputeServer.ApiAddress(typeof(Mesh), caller);
        }

        /// <summary>
        /// Constructs a planar mesh grid.
        /// </summary>
        /// <param name="plane">Plane of mesh.</param>
        /// <param name="xInterval">Interval describing size and extends of mesh along plane x-direction.</param>
        /// <param name="yInterval">Interval describing size and extends of mesh along plane y-direction.</param>
        /// <param name="xCount">Number of faces in x-direction.</param>
        /// <param name="yCount">Number of faces in y-direction.</param>
        /// <exception cref="ArgumentException">Thrown when plane is a null reference.</exception>
        /// <exception cref="ArgumentException">Thrown when xInterval is a null reference.</exception>
        /// <exception cref="ArgumentException">Thrown when yInterval is a null reference.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when xCount is less than or equal to zero.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when yCount is less than or equal to zero.</exception>
        public static Mesh CreateFromPlane(Plane plane, Interval xInterval, Interval yInterval, int xCount, int yCount)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), plane, xInterval, yInterval, xCount, yCount);
        }

        /// <summary>
        /// Constructs a sub-mesh, that contains a filtered list of faces.
        /// </summary>
        /// <param name="original">The mesh to copy. This item can be null, and in this case an empty mesh is returned.</param>
        /// <param name="inclusion">A series of true and false values, that determine if each face is used in the new mesh.
        /// <para>If the amount does not match the length of the face list, the pattern is repeated. If it exceeds the amount
        /// of faces in the mesh face list, the pattern is truncated. This items can be null or empty, and the mesh will simply be duplicated.</para>
        /// </param>
        public static Mesh CreateFromFilteredFaceList(Mesh original, IEnumerable<bool> inclusion)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), original, inclusion);
        }

        /// <summary>
        /// Constructs a sub-mesh, that contains a filtered list of faces.
        /// </summary>
        /// <param name="original">The mesh to copy. This item can be null, and in this case an empty mesh is returned.</param>
        /// <param name="inclusion">A series of true and false values, that determine if each face is used in the new mesh.
        /// <para>If the amount does not match the length of the face list, the pattern is repeated. If it exceeds the amount
        /// of faces in the mesh face list, the pattern is truncated. This items can be null or empty, and the mesh will simply be duplicated.</para>
        /// </param>
        public static Mesh CreateFromFilteredFaceList(Remote<Mesh> original, IEnumerable<bool> inclusion)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), original, inclusion);
        }

        /// <summary>
        /// Constructs new mesh that matches a bounding box.
        /// </summary>
        /// <param name="box">A box to use for creation.</param>
        /// <param name="xCount">Number of faces in x-direction.</param>
        /// <param name="yCount">Number of faces in y-direction.</param>
        /// <param name="zCount">Number of faces in z-direction.</param>
        /// <returns>A new brep, or null on failure.</returns>
        public static Mesh CreateFromBox(BoundingBox box, int xCount, int yCount, int zCount)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), box, xCount, yCount, zCount);
        }

        /// <summary>
        ///  Constructs new mesh that matches an aligned box.
        /// </summary>
        /// <param name="box">Box to match.</param>
        /// <param name="xCount">Number of faces in x-direction.</param>
        /// <param name="yCount">Number of faces in y-direction.</param>
        /// <param name="zCount">Number of faces in z-direction.</param>
        /// <returns></returns>
        public static Mesh CreateFromBox(Box box, int xCount, int yCount, int zCount)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), box, xCount, yCount, zCount);
        }

        /// <summary>
        /// Constructs new mesh from 8 corner points.
        /// </summary>
        /// <param name="corners">
        /// 8 points defining the box corners arranged as the vN labels indicate.
        /// <pre>
        /// <para>v7_____________v6</para>
        /// <para>|\                         |\</para>
        /// <para>| \                        | \</para>
        /// <para>|  \ _____________\</para>
        /// <para>|   v4                 |   v5</para>
        /// <para>|   |                  |   |</para>
        /// <para>|   |                  |   |</para>
        /// <para>v3--|----------v2  |</para>
        /// <para> \  |                   \  |</para>
        /// <para>  \ |                        \ |</para>
        /// <para>   \|                         \|</para>
        /// <para>        v0_____________v1</para>
        /// </pre>
        /// </param>
        /// <param name="xCount">Number of faces in x-direction.</param>
        /// <param name="yCount">Number of faces in y-direction.</param>
        /// <param name="zCount">Number of faces in z-direction.</param>
        /// <returns>A new brep, or null on failure.</returns>
        /// <returns>A new box mesh, on null on error.</returns>
        public static Mesh CreateFromBox(IEnumerable<Point3d> corners, int xCount, int yCount, int zCount)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), corners, xCount, yCount, zCount);
        }

        /// <summary>
        /// Constructs a mesh sphere.
        /// </summary>
        /// <param name="sphere">Base sphere for mesh.</param>
        /// <param name="xCount">Number of faces in the around direction.</param>
        /// <param name="yCount">Number of faces in the top-to-bottom direction.</param>
        /// <exception cref="ArgumentException">Thrown when sphere is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when xCount is less than or equal to two.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when yCount is less than or equal to two.</exception>
        /// <returns></returns>
        public static Mesh CreateFromSphere(Sphere sphere, int xCount, int yCount)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), sphere, xCount, yCount);
        }

        /// <summary>
        /// Constructs a icospherical mesh. A mesh icosphere differs from a standard
        /// UV mesh sphere in that it's vertices are evenly distributed. A mesh icosphere
        /// starts from an icosahedron (a regular polyhedron with 20 equilateral triangles).
        /// It is then refined by splitting each triangle into 4 smaller triangles.
        /// This splitting can be done several times.
        /// </summary>
        /// <param name="sphere">The input sphere provides the orienting plane and radius.</param>
        /// <param name="subdivisions">
        /// The number of times you want the faces split, where 0  &lt;= subdivisions &lt;= 7. 
        /// Note, the total number of mesh faces produces is: 20 * (4 ^ subdivisions)
        /// </param>
        /// <returns>A welded mesh icosphere if successful, or null on failure.</returns>
        public static Mesh CreateIcoSphere(Sphere sphere, int subdivisions)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), sphere, subdivisions);
        }

        /// <summary>
        /// Constructs a quad mesh sphere. A quad mesh sphere differs from a standard
        /// UV mesh sphere in that it's vertices are evenly distributed. A quad mesh sphere
        /// starts from a cube (a regular polyhedron with 6 square sides).
        /// It is then refined by splitting each quad into 4 smaller quads.
        /// This splitting can be done several times.
        /// </summary>
        /// <param name="sphere">The input sphere provides the orienting plane and radius.</param>
        /// <param name="subdivisions">
        /// The number of times you want the faces split, where 0  &lt;= subdivisions &lt;= 8. 
        /// Note, the total number of mesh faces produces is: 6 * (4 ^ subdivisions)
        /// </param>
        /// <returns>A welded quad mesh sphere if successful, or null on failure.</returns>
        public static Mesh CreateQuadSphere(Sphere sphere, int subdivisions)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), sphere, subdivisions);
        }

        /// <summary>Constructs a capped mesh cylinder.</summary>
        /// <param name="cylinder"></param>
        /// <param name="vertical">Number of faces in the top-to-bottom direction.</param>
        /// <param name="around">Number of faces around the cylinder.</param>
        /// <exception cref="ArgumentException">Thrown when cylinder is invalid.</exception>
        /// <returns>Returns a mesh cylinder if successful, null otherwise.</returns>
        public static Mesh CreateFromCylinder(Cylinder cylinder, int vertical, int around)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), cylinder, vertical, around);
        }

        /// <summary>Constructs a mesh cylinder.</summary>
        /// <param name="cylinder"></param>
        /// <param name="vertical">Number of faces in the top-to-bottom direction.</param>
        /// <param name="around">Number of faces around the cylinder.</param>
        /// <param name="capBottom">If true end at Cylinder.Height1 should be capped.</param>
        /// <param name="capTop">If true end at Cylinder.Height2 should be capped.</param>
        /// <exception cref="ArgumentException">Thrown when cylinder is invalid.</exception>
        /// <returns>Returns a mesh cylinder if successful, null otherwise.</returns>
        public static Mesh CreateFromCylinder(Cylinder cylinder, int vertical, int around, bool capBottom, bool capTop)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), cylinder, vertical, around, capBottom, capTop);
        }

        /// <summary>Constructs a mesh cylinder.</summary>
        /// <param name="cylinder"></param>
        /// <param name="vertical">Number of faces in the top-to-bottom direction.</param>
        /// <param name="around">Number of faces around the cylinder.</param>
        /// <param name="capBottom">If true end at Cylinder.Height1 should be capped.</param>
        /// <param name="capTop">If true end at Cylinder.Height2 should be capped.</param>
        /// <param name="quadCaps">If true and it's possible to make quad caps, i.e.. around is even, then caps will have quad faces.</param>
        /// <exception cref="ArgumentException">Thrown when cylinder is invalid.</exception>
        /// <returns>Returns a mesh cylinder if successful, null otherwise.</returns>
        public static Mesh CreateFromCylinder(Cylinder cylinder, int vertical, int around, bool capBottom, bool capTop, bool quadCaps)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), cylinder, vertical, around, capBottom, capTop, quadCaps);
        }

        /// <summary>Constructs a solid mesh cone.</summary>
        /// <param name="cone"></param>
        /// <param name="vertical">Number of faces in the top-to-bottom direction.</param>
        /// <param name="around">Number of faces around the cone.</param>
        /// <exception cref="ArgumentException">Thrown when cone is invalid.</exception>
        /// <returns>A valid mesh if successful.</returns>
        public static Mesh CreateFromCone(Cone cone, int vertical, int around)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), cone, vertical, around);
        }

        /// <summary>Constructs a mesh cone.</summary>
        /// <param name="cone"></param>
        /// <param name="vertical">Number of faces in the top-to-bottom direction.</param>
        /// <param name="around">Number of faces around the cone.</param>
        /// <param name="solid">If false the mesh will be open with no faces on the circular planar portion.</param>
        /// <exception cref="ArgumentException">Thrown when cone is invalid.</exception>
        /// <returns>A valid mesh if successful.</returns>
        public static Mesh CreateFromCone(Cone cone, int vertical, int around, bool solid)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), cone, vertical, around, solid);
        }

        /// <summary>Constructs a mesh cone.</summary>
        /// <param name="cone"></param>
        /// <param name="vertical">Number of faces in the top-to-bottom direction.</param>
        /// <param name="around">Number of faces around the cone.</param>
        /// <param name="solid">If false the mesh will be open with no faces on the circular planar portion.</param>
        /// <param name="quadCaps">If true and it's possible to make quad caps, i.e.. around is even, then caps will have quad faces.</param>
        /// <exception cref="ArgumentException">Thrown when cone is invalid.</exception>
        /// <returns>A valid mesh if successful.</returns>
        public static Mesh CreateFromCone(Cone cone, int vertical, int around, bool solid, bool quadCaps)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), cone, vertical, around, solid, quadCaps);
        }

        /// <summary>
        /// Constructs a mesh torus.
        /// </summary>
        /// <param name="torus">The torus.</param>
        /// <param name="vertical">Number of faces in the top-to-bottom direction.</param>
        /// <param name="around">Number of faces around the torus.</param>
        /// <exception cref="ArgumentException">Thrown when torus is invalid.</exception>
        /// <returns>Returns a mesh torus if successful, null otherwise.</returns>
        public static Mesh CreateFromTorus(Torus torus, int vertical, int around)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), torus, vertical, around);
        }

        /// <summary>
        /// Do not use this overload. Use version that takes a tolerance parameter instead.
        /// </summary>
        /// <param name="boundary">Do not use.</param>
        /// <param name="parameters">Do not use.</param>
        /// <returns>
        /// Do not use.
        /// </returns>
        /// <deprecated>6.0</deprecated>
        public static Mesh CreateFromPlanarBoundary(Curve boundary, MeshingParameters parameters)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), boundary, parameters);
        }

        /// <summary>
        /// Do not use this overload. Use version that takes a tolerance parameter instead.
        /// </summary>
        /// <param name="boundary">Do not use.</param>
        /// <param name="parameters">Do not use.</param>
        /// <returns>
        /// Do not use.
        /// </returns>
        /// <deprecated>6.0</deprecated>
        public static Mesh CreateFromPlanarBoundary(Remote<Curve> boundary, MeshingParameters parameters)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), boundary, parameters);
        }

        /// <summary>
        /// Attempts to construct a mesh from a closed planar curve.RhinoMakePlanarMeshes
        /// </summary>
        /// <param name="boundary">must be a closed planar curve.</param>
        /// <param name="parameters">parameters used for creating the mesh.</param>
        /// <param name="tolerance">Tolerance to use during operation.</param>
        /// <returns>
        /// New mesh on success or null on failure.
        /// </returns>
        public static Mesh CreateFromPlanarBoundary(Curve boundary, MeshingParameters parameters, double tolerance)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), boundary, parameters, tolerance);
        }

        /// <summary>
        /// Attempts to construct a mesh from a closed planar curve.RhinoMakePlanarMeshes
        /// </summary>
        /// <param name="boundary">must be a closed planar curve.</param>
        /// <param name="parameters">parameters used for creating the mesh.</param>
        /// <param name="tolerance">Tolerance to use during operation.</param>
        /// <returns>
        /// New mesh on success or null on failure.
        /// </returns>
        public static Mesh CreateFromPlanarBoundary(Remote<Curve> boundary, MeshingParameters parameters, double tolerance)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), boundary, parameters, tolerance);
        }

        /// <summary>
        /// Attempts to create a Mesh that is a triangulation of a simple closed polyline that projects onto a plane.
        /// </summary>
        /// <param name="polyline">must be closed</param>
        /// <returns>
        /// New mesh on success or null on failure.
        /// </returns>
        public static Mesh CreateFromClosedPolyline(Polyline polyline)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), polyline);
        }

        /// <summary>
        /// Attempts to create a mesh that is a triangulation of a list of points, projected on a plane,
        /// including its holes and fixed edges.
        /// </summary>
        /// <param name="points">A list, an array or any enumerable of points.</param>
        /// <param name="plane">A plane.</param>
        /// <param name="allowNewVertices">If true, the mesh might have more vertices than the list of input points,
        /// if doing so will improve long thin triangles.</param>
        /// <param name="edges">A list of polylines, or other lists of points representing edges.
        /// This can be null. If nested enumerable items are null, they will be discarded.</param>
        /// <returns>A new mesh, or null if not successful.</returns>
        /// <exception cref="ArgumentNullException">If points is null.</exception>
        /// <exception cref="ArgumentException">If plane is not valid.</exception>
        public static Mesh CreateFromTessellation(IEnumerable<Point3d> points, IEnumerable<IEnumerable<Point3d>> edges, Plane plane, bool allowNewVertices)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), points, edges, plane, allowNewVertices);
        }

        /// <summary>
        /// Constructs a mesh from a brep.
        /// </summary>
        /// <param name="brep">Brep to approximate.</param>
        /// <returns>An array of meshes.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_tightboundingbox.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_tightboundingbox.cs' lang='cs'/>
        /// <code source='examples\py\ex_tightboundingbox.py' lang='py'/>
        /// </example>
        /// <deprecated>6.0</deprecated>
        public static Mesh[] CreateFromBrep(Brep brep)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), brep);
        }

        /// <summary>
        /// Constructs a mesh from a brep.
        /// </summary>
        /// <param name="brep">Brep to approximate.</param>
        /// <returns>An array of meshes.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_tightboundingbox.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_tightboundingbox.cs' lang='cs'/>
        /// <code source='examples\py\ex_tightboundingbox.py' lang='py'/>
        /// </example>
        /// <deprecated>6.0</deprecated>
        public static Mesh[] CreateFromBrep(Remote<Brep> brep)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), brep);
        }

        /// <summary>
        /// Constructs a mesh from a brep.
        /// </summary>
        /// <param name="brep">Brep to approximate.</param>
        /// <param name="meshingParameters">Parameters to use during meshing.</param>
        /// <returns>An array of meshes.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_createmeshfrombrep.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_createmeshfrombrep.cs' lang='cs'/>
        /// <code source='examples\py\ex_createmeshfrombrep.py' lang='py'/>
        /// </example>
        public static Mesh[] CreateFromBrep(Brep brep, MeshingParameters meshingParameters)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), brep, meshingParameters);
        }

        /// <summary>
        /// Constructs a mesh from a brep.
        /// </summary>
        /// <param name="brep">Brep to approximate.</param>
        /// <param name="meshingParameters">Parameters to use during meshing.</param>
        /// <returns>An array of meshes.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_createmeshfrombrep.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_createmeshfrombrep.cs' lang='cs'/>
        /// <code source='examples\py\ex_createmeshfrombrep.py' lang='py'/>
        /// </example>
        public static Mesh[] CreateFromBrep(Remote<Brep> brep, MeshingParameters meshingParameters)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), brep, meshingParameters);
        }

        /// <summary>
        /// Constructs a mesh from a surface
        /// </summary>
        /// <param name="surface">Surface to approximate</param>
        /// <returns>New mesh representing the surface</returns>
        public static Mesh CreateFromSurface(Surface surface)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), surface);
        }

        /// <summary>
        /// Constructs a mesh from a surface
        /// </summary>
        /// <param name="surface">Surface to approximate</param>
        /// <returns>New mesh representing the surface</returns>
        public static Mesh CreateFromSurface(Remote<Surface> surface)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), surface);
        }

        /// <summary>
        /// Constructs a mesh from a surface
        /// </summary>
        /// <param name="surface">Surface to approximate</param>
        /// <param name="meshingParameters">settings used to create the mesh</param>
        /// <returns>New mesh representing the surface</returns>
        public static Mesh CreateFromSurface(Surface surface, MeshingParameters meshingParameters)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), surface, meshingParameters);
        }

        /// <summary>
        /// Constructs a mesh from a surface
        /// </summary>
        /// <param name="surface">Surface to approximate</param>
        /// <param name="meshingParameters">settings used to create the mesh</param>
        /// <returns>New mesh representing the surface</returns>
        public static Mesh CreateFromSurface(Remote<Surface> surface, MeshingParameters meshingParameters)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), surface, meshingParameters);
        }

        /// <summary>
        /// Create a mesh from a SubD limit surface
        /// </summary>
        /// <param name="subd"></param>
        /// <param name="displayDensity"></param>
        /// <returns></returns>
        public static Mesh CreateFromSubD(SubD subd, int displayDensity)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), subd, displayDensity);
        }

        /// <summary>
        /// Construct a mesh patch from a variety of input geometry.
        /// </summary>
        /// <param name="outerBoundary">(optional: can be null) Outer boundary
        /// polyline, if provided this will become the outer boundary of the
        /// resulting mesh. Any of the input that is completely outside the outer
        /// boundary will be ignored and have no impact on the result. If any of
        /// the input intersects the outer boundary the result will be
        /// unpredictable and is likely to not include the entire outer boundary.
        /// </param>
        /// <param name="angleToleranceRadians">
        /// Maximum angle between unit tangents and adjacent vertices. Used to
        /// divide curve inputs that cannot otherwise be represented as a polyline.
        /// </param>
        /// <param name="innerBoundaryCurves">
        /// (optional: can be null) Polylines to create holes in the output mesh.
        /// If innerBoundaryCurves are the only input then the result may be null
        /// if trimback is set to false (see comments for trimback) because the
        /// resulting mesh could be invalid (all faces created contained vertexes
        /// from the perimeter boundary).
        /// </param>
        /// <param name="pullbackSurface">
        /// (optional: can be null) Initial surface where 3d input will be pulled
        /// to make a 2d representation used by the function that generates the mesh.
        /// Providing a pullbackSurface can be helpful when it is similar in shape
        /// to the pattern of the input, the pulled 2d points will be a better
        /// representation of the 3d points. If all of the input is more or less
        /// coplanar to start with, providing pullbackSurface has no real benefit.
        /// </param>
        /// <param name="innerBothSideCurves">
        /// (optional: can be null) These polylines will create faces on both sides
        /// of the edge. If there are only input points(innerPoints) there is no
        /// way to guarantee a triangulation that will create an edge between two
        /// particular points. Adding a line, or polyline, to innerBothsideCurves
        /// that includes points from innerPoints will help guide the triangulation.
        /// </param>
        /// <param name="innerPoints">
        /// (optional: can be null) Points to be used to generate the mesh. If
        /// outerBoundary is not null, points outside of that boundary after it has
        /// been pulled to pullbackSurface (or the best plane through the input if
        /// pullbackSurface is null) will be ignored.
        /// </param>
        /// <param name="trimback">
        /// Only used when a outerBoundary has not been provided. When that is the
        /// case, the function uses the perimeter of the surface as the outer boundary
        /// instead. If true, any face of the resulting triangulated mesh that
        /// contains a vertex of the perimeter boundary will be removed.
        /// </param>
        /// <param name="divisions">
        /// Only used when a outerBoundary has not been provided. When that is the
        /// case, division becomes the number of divisions each side of the surface's
        /// perimeter will be divided into to create an outer boundary to work with.
        /// </param>
        /// <returns>mesh on success; null on failure</returns>
        public static Mesh CreatePatch(Polyline outerBoundary, double angleToleranceRadians, Surface pullbackSurface, IEnumerable<Curve> innerBoundaryCurves, IEnumerable<Curve> innerBothSideCurves, IEnumerable<Point3d> innerPoints, bool trimback, int divisions)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), outerBoundary, angleToleranceRadians, pullbackSurface, innerBoundaryCurves, innerBothSideCurves, innerPoints, trimback, divisions);
        }

        /// <summary>
        /// Construct a mesh patch from a variety of input geometry.
        /// </summary>
        /// <param name="outerBoundary">(optional: can be null) Outer boundary
        /// polyline, if provided this will become the outer boundary of the
        /// resulting mesh. Any of the input that is completely outside the outer
        /// boundary will be ignored and have no impact on the result. If any of
        /// the input intersects the outer boundary the result will be
        /// unpredictable and is likely to not include the entire outer boundary.
        /// </param>
        /// <param name="angleToleranceRadians">
        /// Maximum angle between unit tangents and adjacent vertices. Used to
        /// divide curve inputs that cannot otherwise be represented as a polyline.
        /// </param>
        /// <param name="innerBoundaryCurves">
        /// (optional: can be null) Polylines to create holes in the output mesh.
        /// If innerBoundaryCurves are the only input then the result may be null
        /// if trimback is set to false (see comments for trimback) because the
        /// resulting mesh could be invalid (all faces created contained vertexes
        /// from the perimeter boundary).
        /// </param>
        /// <param name="pullbackSurface">
        /// (optional: can be null) Initial surface where 3d input will be pulled
        /// to make a 2d representation used by the function that generates the mesh.
        /// Providing a pullbackSurface can be helpful when it is similar in shape
        /// to the pattern of the input, the pulled 2d points will be a better
        /// representation of the 3d points. If all of the input is more or less
        /// coplanar to start with, providing pullbackSurface has no real benefit.
        /// </param>
        /// <param name="innerBothSideCurves">
        /// (optional: can be null) These polylines will create faces on both sides
        /// of the edge. If there are only input points(innerPoints) there is no
        /// way to guarantee a triangulation that will create an edge between two
        /// particular points. Adding a line, or polyline, to innerBothsideCurves
        /// that includes points from innerPoints will help guide the triangulation.
        /// </param>
        /// <param name="innerPoints">
        /// (optional: can be null) Points to be used to generate the mesh. If
        /// outerBoundary is not null, points outside of that boundary after it has
        /// been pulled to pullbackSurface (or the best plane through the input if
        /// pullbackSurface is null) will be ignored.
        /// </param>
        /// <param name="trimback">
        /// Only used when a outerBoundary has not been provided. When that is the
        /// case, the function uses the perimeter of the surface as the outer boundary
        /// instead. If true, any face of the resulting triangulated mesh that
        /// contains a vertex of the perimeter boundary will be removed.
        /// </param>
        /// <param name="divisions">
        /// Only used when a outerBoundary has not been provided. When that is the
        /// case, division becomes the number of divisions each side of the surface's
        /// perimeter will be divided into to create an outer boundary to work with.
        /// </param>
        /// <returns>mesh on success; null on failure</returns>
        public static Mesh CreatePatch(Polyline outerBoundary, double angleToleranceRadians, Remote<Surface> pullbackSurface, Remote<IEnumerable<Curve>> innerBoundaryCurves, Remote<IEnumerable<Curve>> innerBothSideCurves, IEnumerable<Point3d> innerPoints, bool trimback, int divisions)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), outerBoundary, angleToleranceRadians, pullbackSurface, innerBoundaryCurves, innerBothSideCurves, innerPoints, trimback, divisions);
        }

        /// <summary>
        /// Computes the solid union of a set of meshes.
        /// </summary>
        /// <param name="meshes">Meshes to union.</param>
        /// <returns>An array of Mesh results or null on failure.</returns>
        public static Mesh[] CreateBooleanUnion(IEnumerable<Mesh> meshes)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), meshes);
        }

        /// <summary>
        /// Computes the solid union of a set of meshes.
        /// </summary>
        /// <param name="meshes">Meshes to union.</param>
        /// <returns>An array of Mesh results or null on failure.</returns>
        public static Mesh[] CreateBooleanUnion(Remote<IEnumerable<Mesh>> meshes)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), meshes);
        }

        /// <summary>
        /// Computes the solid difference of two sets of Meshes.
        /// </summary>
        /// <param name="firstSet">First set of Meshes (the set to subtract from).</param>
        /// <param name="secondSet">Second set of Meshes (the set to subtract).</param>
        /// <returns>An array of Mesh results or null on failure.</returns>
        public static Mesh[] CreateBooleanDifference(IEnumerable<Mesh> firstSet, IEnumerable<Mesh> secondSet)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), firstSet, secondSet);
        }

        /// <summary>
        /// Computes the solid difference of two sets of Meshes.
        /// </summary>
        /// <param name="firstSet">First set of Meshes (the set to subtract from).</param>
        /// <param name="secondSet">Second set of Meshes (the set to subtract).</param>
        /// <returns>An array of Mesh results or null on failure.</returns>
        public static Mesh[] CreateBooleanDifference(Remote<IEnumerable<Mesh>> firstSet, Remote<IEnumerable<Mesh>> secondSet)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), firstSet, secondSet);
        }

        /// <summary>
        /// Computes the solid intersection of two sets of meshes.
        /// </summary>
        /// <param name="firstSet">First set of Meshes.</param>
        /// <param name="secondSet">Second set of Meshes.</param>
        /// <returns>An array of Mesh results or null on failure.</returns>
        public static Mesh[] CreateBooleanIntersection(IEnumerable<Mesh> firstSet, IEnumerable<Mesh> secondSet)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), firstSet, secondSet);
        }

        /// <summary>
        /// Computes the solid intersection of two sets of meshes.
        /// </summary>
        /// <param name="firstSet">First set of Meshes.</param>
        /// <param name="secondSet">Second set of Meshes.</param>
        /// <returns>An array of Mesh results or null on failure.</returns>
        public static Mesh[] CreateBooleanIntersection(Remote<IEnumerable<Mesh>> firstSet, Remote<IEnumerable<Mesh>> secondSet)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), firstSet, secondSet);
        }

        /// <summary>
        /// Splits a set of meshes with another set.
        /// </summary>
        /// <param name="meshesToSplit">A list, an array, or any enumerable set of meshes to be split. If this is null, null will be returned.</param>
        /// <param name="meshSplitters">A list, an array, or any enumerable set of meshes that cut. If this is null, null will be returned.</param>
        /// <returns>A new mesh array, or null on error.</returns>
        public static Mesh[] CreateBooleanSplit(IEnumerable<Mesh> meshesToSplit, IEnumerable<Mesh> meshSplitters)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), meshesToSplit, meshSplitters);
        }

        /// <summary>
        /// Splits a set of meshes with another set.
        /// </summary>
        /// <param name="meshesToSplit">A list, an array, or any enumerable set of meshes to be split. If this is null, null will be returned.</param>
        /// <param name="meshSplitters">A list, an array, or any enumerable set of meshes that cut. If this is null, null will be returned.</param>
        /// <returns>A new mesh array, or null on error.</returns>
        public static Mesh[] CreateBooleanSplit(Remote<IEnumerable<Mesh>> meshesToSplit, Remote<IEnumerable<Mesh>> meshSplitters)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), meshesToSplit, meshSplitters);
        }

        /// <summary>
        /// Constructs a new mesh pipe from a curve.
        /// </summary>
        /// <param name="curve">A curve to pipe.</param>
        /// <param name="radius">The radius of the pipe.</param>
        /// <param name="segments">The number of segments in the pipe.</param>
        /// <param name="accuracy">The accuracy of the pipe.</param>
        /// <param name="capType">The type of cap to be created at the end of the pipe.</param>
        /// <param name="faceted">Specifies whether the pipe is faceted, or not.</param>
        /// <param name="intervals">A series of intervals to pipe. This value can be null.</param>
        /// <returns>A new mesh, or null on failure.</returns>
        public static Mesh CreateFromCurvePipe(Curve curve, double radius, int segments, int accuracy, MeshPipeCapStyle capType, bool faceted, IEnumerable<Interval> intervals = null)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), curve, radius, segments, accuracy, capType, faceted, intervals);
        }

        /// <summary>
        /// Constructs a new mesh pipe from a curve.
        /// </summary>
        /// <param name="curve">A curve to pipe.</param>
        /// <param name="radius">The radius of the pipe.</param>
        /// <param name="segments">The number of segments in the pipe.</param>
        /// <param name="accuracy">The accuracy of the pipe.</param>
        /// <param name="capType">The type of cap to be created at the end of the pipe.</param>
        /// <param name="faceted">Specifies whether the pipe is faceted, or not.</param>
        /// <param name="intervals">A series of intervals to pipe. This value can be null.</param>
        /// <returns>A new mesh, or null on failure.</returns>
        public static Mesh CreateFromCurvePipe(Remote<Curve> curve, double radius, int segments, int accuracy, MeshPipeCapStyle capType, bool faceted, IEnumerable<Interval> intervals = null)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), curve, radius, segments, accuracy, capType, faceted, intervals);
        }

        /// <summary>
        /// Constructs a new extrusion from a curve.
        /// </summary>
        /// <param name="curve">A curve to extrude.</param>
        /// <param name="direction">The direction of extrusion.</param>
        /// <param name="parameters">The parameters of meshing.</param>
        /// <param name="boundingBox">The bounding box controls the length of the extrusion.</param>
        /// <returns>A new mesh, or null on failure.</returns>
        public static Mesh CreateFromCurveExtrusion(Curve curve, Vector3d direction, MeshingParameters parameters, BoundingBox boundingBox)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), curve, direction, parameters, boundingBox);
        }

        /// <summary>
        /// Constructs a new extrusion from a curve.
        /// </summary>
        /// <param name="curve">A curve to extrude.</param>
        /// <param name="direction">The direction of extrusion.</param>
        /// <param name="parameters">The parameters of meshing.</param>
        /// <param name="boundingBox">The bounding box controls the length of the extrusion.</param>
        /// <returns>A new mesh, or null on failure.</returns>
        public static Mesh CreateFromCurveExtrusion(Remote<Curve> curve, Vector3d direction, MeshingParameters parameters, BoundingBox boundingBox)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), curve, direction, parameters, boundingBox);
        }

        /// <summary>Repairs meshes with vertices that are too near, using a tolerance value.</summary>
        /// <param name="meshes">The meshes to be repaired.</param>
        /// <param name="tolerance">A minimum distance for clean vertices.</param>
        /// <returns>A valid meshes array if successful. If no change was required, some meshes can be null. Otherwise, null, when no changes were done.</returns>
        /// <remarks><seealso cref="RequireIterativeCleanup"/></remarks>
        public static Mesh[] CreateFromIterativeCleanup(IEnumerable<Mesh> meshes, double tolerance)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), meshes, tolerance);
        }

        /// <summary>Repairs meshes with vertices that are too near, using a tolerance value.</summary>
        /// <param name="meshes">The meshes to be repaired.</param>
        /// <param name="tolerance">A minimum distance for clean vertices.</param>
        /// <returns>A valid meshes array if successful. If no change was required, some meshes can be null. Otherwise, null, when no changes were done.</returns>
        /// <remarks><seealso cref="RequireIterativeCleanup"/></remarks>
        public static Mesh[] CreateFromIterativeCleanup(Remote<IEnumerable<Mesh>> meshes, double tolerance)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), meshes, tolerance);
        }

        /// <summary>
        /// Analyzes some meshes, and determines if a pass of CreateFromIterativeCleanup would change the array.
        /// <para>All available cleanup steps are used. Currently available cleanup steps are:</para>
        /// <para>- mending of single precision coincidence even though double precision vertices differ.</para>
        /// <para>- union of nearly identical vertices, irrespectively of their origin.</para>
        /// <para>- removal of t-joints along edges.</para>
        /// </summary>
        /// <param name="meshes">A list, and array or any enumerable of meshes.</param>
        /// <param name="tolerance">A 3d distance. This is usually a value of about 10e-7 magnitude.</param>
        /// <returns>True if meshes would be changed, otherwise false.</returns>
        /// <remarks><seealso cref="Rhino.Geometry.Mesh.CreateFromIterativeCleanup"/></remarks>
        public static bool RequireIterativeCleanup(IEnumerable<Mesh> meshes, double tolerance)
        {
            return ComputeServer.Post<bool>(ApiAddress(), meshes, tolerance);
        }

        /// <summary>
        /// Analyzes some meshes, and determines if a pass of CreateFromIterativeCleanup would change the array.
        /// <para>All available cleanup steps are used. Currently available cleanup steps are:</para>
        /// <para>- mending of single precision coincidence even though double precision vertices differ.</para>
        /// <para>- union of nearly identical vertices, irrespectively of their origin.</para>
        /// <para>- removal of t-joints along edges.</para>
        /// </summary>
        /// <param name="meshes">A list, and array or any enumerable of meshes.</param>
        /// <param name="tolerance">A 3d distance. This is usually a value of about 10e-7 magnitude.</param>
        /// <returns>True if meshes would be changed, otherwise false.</returns>
        /// <remarks><seealso cref="Rhino.Geometry.Mesh.CreateFromIterativeCleanup"/></remarks>
        public static bool RequireIterativeCleanup(Remote<IEnumerable<Mesh>> meshes, double tolerance)
        {
            return ComputeServer.Post<bool>(ApiAddress(), meshes, tolerance);
        }

        /// <summary>
        /// Compute volume of the mesh. 
        /// </summary>
        /// <returns>Volume of the mesh.</returns>
        public static double Volume(this Mesh mesh)
        {
            return ComputeServer.Post<double>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Compute volume of the mesh. 
        /// </summary>
        /// <returns>Volume of the mesh.</returns>
        public static double Volume(Remote<Mesh> mesh)
        {
            return ComputeServer.Post<double>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Determines if a point is inside a solid mesh.
        /// </summary>
        /// <param name="point">3d point to test.</param>
        /// <param name="tolerance">
        /// (&gt;=0) 3d distance tolerance used for ray-mesh intersection
        /// and determining strict inclusion.
        /// </param>
        /// <param name="strictlyIn">
        /// If strictlyIn is true, then point must be inside mesh by at least
        /// tolerance in order for this function to return true.
        /// If strictlyIn is false, then this function will return true if
        /// point is inside or the distance from point to a mesh face is &lt;= tolerance.
        /// </param>
        /// <returns>
        /// true if point is inside the solid mesh, false if not.
        /// </returns>
        /// <remarks>
        /// The caller is responsible for making certain the mesh is solid before
        /// calling this function. If the mesh is not solid, the behavior is unpredictable.
        /// </remarks>
        public static bool IsPointInside(this Mesh mesh, Point3d point, double tolerance, bool strictlyIn)
        {
            return ComputeServer.Post<bool>(ApiAddress(), mesh, point, tolerance, strictlyIn);
        }

        /// <summary>
        /// Determines if a point is inside a solid mesh.
        /// </summary>
        /// <param name="point">3d point to test.</param>
        /// <param name="tolerance">
        /// (&gt;=0) 3d distance tolerance used for ray-mesh intersection
        /// and determining strict inclusion.
        /// </param>
        /// <param name="strictlyIn">
        /// If strictlyIn is true, then point must be inside mesh by at least
        /// tolerance in order for this function to return true.
        /// If strictlyIn is false, then this function will return true if
        /// point is inside or the distance from point to a mesh face is &lt;= tolerance.
        /// </param>
        /// <returns>
        /// true if point is inside the solid mesh, false if not.
        /// </returns>
        /// <remarks>
        /// The caller is responsible for making certain the mesh is solid before
        /// calling this function. If the mesh is not solid, the behavior is unpredictable.
        /// </remarks>
        public static bool IsPointInside(Remote<Mesh> mesh, Point3d point, double tolerance, bool strictlyIn)
        {
            return ComputeServer.Post<bool>(ApiAddress(), mesh, point, tolerance, strictlyIn);
        }

        /// <summary>
        /// Smooths a mesh by averaging the positions of mesh vertices in a specified region.
        /// </summary>
        /// <param name="smoothFactor">The smoothing factor, which controls how much vertices move towards the average of the neighboring vertices.</param>
        /// <param name="bXSmooth">When true vertices move in X axis direction.</param>
        /// <param name="bYSmooth">When true vertices move in Y axis direction.</param>
        /// <param name="bZSmooth">When true vertices move in Z axis direction.</param>
        /// <param name="bFixBoundaries">When true vertices along naked edges will not be modified.</param>
        /// <param name="coordinateSystem">The coordinates to determine the direction of the smoothing.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public static bool Smooth(this Mesh mesh, out Mesh updatedInstance, double smoothFactor, bool bXSmooth, bool bYSmooth, bool bZSmooth, bool bFixBoundaries, SmoothingCoordinateSystem coordinateSystem)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, smoothFactor, bXSmooth, bYSmooth, bZSmooth, bFixBoundaries, coordinateSystem);
        }

        /// <summary>
        /// Smooths a mesh by averaging the positions of mesh vertices in a specified region.
        /// </summary>
        /// <param name="smoothFactor">The smoothing factor, which controls how much vertices move towards the average of the neighboring vertices.</param>
        /// <param name="bXSmooth">When true vertices move in X axis direction.</param>
        /// <param name="bYSmooth">When true vertices move in Y axis direction.</param>
        /// <param name="bZSmooth">When true vertices move in Z axis direction.</param>
        /// <param name="bFixBoundaries">When true vertices along naked edges will not be modified.</param>
        /// <param name="coordinateSystem">The coordinates to determine the direction of the smoothing.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public static bool Smooth(Remote<Mesh> mesh, out Mesh updatedInstance, double smoothFactor, bool bXSmooth, bool bYSmooth, bool bZSmooth, bool bFixBoundaries, SmoothingCoordinateSystem coordinateSystem)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, smoothFactor, bXSmooth, bYSmooth, bZSmooth, bFixBoundaries, coordinateSystem);
        }

        /// <summary>
        /// Smooths a mesh by averaging the positions of mesh vertices in a specified region.
        /// </summary>
        /// <param name="smoothFactor">The smoothing factor, which controls how much vertices move towards the average of the neighboring vertices.</param>
        /// <param name="bXSmooth">When true vertices move in X axis direction.</param>
        /// <param name="bYSmooth">When true vertices move in Y axis direction.</param>
        /// <param name="bZSmooth">When true vertices move in Z axis direction.</param>
        /// <param name="bFixBoundaries">When true vertices along naked edges will not be modified.</param>
        /// <param name="coordinateSystem">The coordinates to determine the direction of the smoothing.</param>
        /// <param name="plane">If SmoothingCoordinateSystem.CPlane specified, then the construction plane.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public static bool Smooth(this Mesh mesh, out Mesh updatedInstance, double smoothFactor, bool bXSmooth, bool bYSmooth, bool bZSmooth, bool bFixBoundaries, SmoothingCoordinateSystem coordinateSystem, Plane plane)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, smoothFactor, bXSmooth, bYSmooth, bZSmooth, bFixBoundaries, coordinateSystem, plane);
        }

        /// <summary>
        /// Smooths a mesh by averaging the positions of mesh vertices in a specified region.
        /// </summary>
        /// <param name="smoothFactor">The smoothing factor, which controls how much vertices move towards the average of the neighboring vertices.</param>
        /// <param name="bXSmooth">When true vertices move in X axis direction.</param>
        /// <param name="bYSmooth">When true vertices move in Y axis direction.</param>
        /// <param name="bZSmooth">When true vertices move in Z axis direction.</param>
        /// <param name="bFixBoundaries">When true vertices along naked edges will not be modified.</param>
        /// <param name="coordinateSystem">The coordinates to determine the direction of the smoothing.</param>
        /// <param name="plane">If SmoothingCoordinateSystem.CPlane specified, then the construction plane.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public static bool Smooth(Remote<Mesh> mesh, out Mesh updatedInstance, double smoothFactor, bool bXSmooth, bool bYSmooth, bool bZSmooth, bool bFixBoundaries, SmoothingCoordinateSystem coordinateSystem, Plane plane)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, smoothFactor, bXSmooth, bYSmooth, bZSmooth, bFixBoundaries, coordinateSystem, plane);
        }

        /// <summary>
        /// Smooths part of a mesh by averaging the positions of mesh vertices in a specified region.
        /// </summary>
        /// <param name="vertexIndices">The mesh vertex indices that specify the part of the mesh to smooth.</param>
        /// <param name="smoothFactor">The smoothing factor, which controls how much vertices move towards the average of the neighboring vertices.</param>
        /// <param name="bXSmooth">When true vertices move in X axis direction.</param>
        /// <param name="bYSmooth">When true vertices move in Y axis direction.</param>
        /// <param name="bZSmooth">When true vertices move in Z axis direction.</param>
        /// <param name="bFixBoundaries">When true vertices along naked edges will not be modified.</param>
        /// <param name="coordinateSystem">The coordinates to determine the direction of the smoothing.</param>
        /// <param name="plane">If SmoothingCoordinateSystem.CPlane specified, then the construction plane.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public static bool Smooth(this Mesh mesh, out Mesh updatedInstance, IEnumerable<int> vertexIndices, double smoothFactor, bool bXSmooth, bool bYSmooth, bool bZSmooth, bool bFixBoundaries, SmoothingCoordinateSystem coordinateSystem, Plane plane)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, vertexIndices, smoothFactor, bXSmooth, bYSmooth, bZSmooth, bFixBoundaries, coordinateSystem, plane);
        }

        /// <summary>
        /// Smooths part of a mesh by averaging the positions of mesh vertices in a specified region.
        /// </summary>
        /// <param name="vertexIndices">The mesh vertex indices that specify the part of the mesh to smooth.</param>
        /// <param name="smoothFactor">The smoothing factor, which controls how much vertices move towards the average of the neighboring vertices.</param>
        /// <param name="bXSmooth">When true vertices move in X axis direction.</param>
        /// <param name="bYSmooth">When true vertices move in Y axis direction.</param>
        /// <param name="bZSmooth">When true vertices move in Z axis direction.</param>
        /// <param name="bFixBoundaries">When true vertices along naked edges will not be modified.</param>
        /// <param name="coordinateSystem">The coordinates to determine the direction of the smoothing.</param>
        /// <param name="plane">If SmoothingCoordinateSystem.CPlane specified, then the construction plane.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public static bool Smooth(Remote<Mesh> mesh, out Mesh updatedInstance, IEnumerable<int> vertexIndices, double smoothFactor, bool bXSmooth, bool bYSmooth, bool bZSmooth, bool bFixBoundaries, SmoothingCoordinateSystem coordinateSystem, Plane plane)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, vertexIndices, smoothFactor, bXSmooth, bYSmooth, bZSmooth, bFixBoundaries, coordinateSystem, plane);
        }

        /// <summary>
        /// Makes sure that faces sharing an edge and having a difference of normal greater
        /// than or equal to angleToleranceRadians have unique vertexes along that edge,
        /// adding vertices if necessary.
        /// </summary>
        /// <param name="angleToleranceRadians">Angle at which to make unique vertices.</param>
        /// <param name="modifyNormals">
        /// Determines whether new vertex normals will have the same vertex normal as the original (false)
        /// or vertex normals made from the corresponding face normals (true)
        /// </param>
        public static Mesh Unweld(this Mesh mesh, double angleToleranceRadians, bool modifyNormals)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, angleToleranceRadians, modifyNormals);
        }

        /// <summary>
        /// Makes sure that faces sharing an edge and having a difference of normal greater
        /// than or equal to angleToleranceRadians have unique vertexes along that edge,
        /// adding vertices if necessary.
        /// </summary>
        /// <param name="angleToleranceRadians">Angle at which to make unique vertices.</param>
        /// <param name="modifyNormals">
        /// Determines whether new vertex normals will have the same vertex normal as the original (false)
        /// or vertex normals made from the corresponding face normals (true)
        /// </param>
        public static Mesh Unweld(Remote<Mesh> mesh, double angleToleranceRadians, bool modifyNormals)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, angleToleranceRadians, modifyNormals);
        }

        /// <summary>
        /// Adds creases to a smooth mesh by creating coincident vertices along selected edges.
        /// </summary>
        /// <param name="edgeIndices">An array of mesh topology edge indices.</param>
        /// <param name="modifyNormals">
        /// If true, the vertex normals on each side of the edge take the same value as the face to which they belong, giving the mesh a hard edge look.
        /// If false, each of the vertex normals on either side of the edge is assigned the same value as the original normal that the pair is replacing, keeping a smooth look.
        /// </param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool UnweldEdge(this Mesh mesh, out Mesh updatedInstance, IEnumerable<int> edgeIndices, bool modifyNormals)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, edgeIndices, modifyNormals);
        }

        /// <summary>
        /// Adds creases to a smooth mesh by creating coincident vertices along selected edges.
        /// </summary>
        /// <param name="edgeIndices">An array of mesh topology edge indices.</param>
        /// <param name="modifyNormals">
        /// If true, the vertex normals on each side of the edge take the same value as the face to which they belong, giving the mesh a hard edge look.
        /// If false, each of the vertex normals on either side of the edge is assigned the same value as the original normal that the pair is replacing, keeping a smooth look.
        /// </param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool UnweldEdge(Remote<Mesh> mesh, out Mesh updatedInstance, IEnumerable<int> edgeIndices, bool modifyNormals)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, edgeIndices, modifyNormals);
        }

        /// <summary>
        /// Ensures that faces sharing a common topological vertex have unique indices into the <see cref="Collections.MeshVertexList"/> collection.
        /// </summary>
        /// <param name="topologyVertexIndices">
        /// Topological vertex indices, from the <see cref="Collections.MeshTopologyVertexList"/> collection, to be unwelded.
        /// Use <see cref="Collections.MeshTopologyVertexList.TopologyVertexIndex"/> to convert from vertex indices to topological vertex indices.
        /// </param>
        /// <param name="modifyNormals">If true, the new vertex normals will be calculated from the face normal.</param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool UnweldVertices(this Mesh mesh, out Mesh updatedInstance, IEnumerable<int> topologyVertexIndices, bool modifyNormals)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, topologyVertexIndices, modifyNormals);
        }

        /// <summary>
        /// Ensures that faces sharing a common topological vertex have unique indices into the <see cref="Collections.MeshVertexList"/> collection.
        /// </summary>
        /// <param name="topologyVertexIndices">
        /// Topological vertex indices, from the <see cref="Collections.MeshTopologyVertexList"/> collection, to be unwelded.
        /// Use <see cref="Collections.MeshTopologyVertexList.TopologyVertexIndex"/> to convert from vertex indices to topological vertex indices.
        /// </param>
        /// <param name="modifyNormals">If true, the new vertex normals will be calculated from the face normal.</param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool UnweldVertices(Remote<Mesh> mesh, out Mesh updatedInstance, IEnumerable<int> topologyVertexIndices, bool modifyNormals)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, topologyVertexIndices, modifyNormals);
        }

        /// <summary>
        /// Makes sure that faces sharing an edge and having a difference of normal greater
        /// than or equal to angleToleranceRadians share vertexes along that edge, vertex normals
        /// are averaged.
        /// </summary>
        /// <param name="angleToleranceRadians">Angle at which to weld vertices.</param>
        public static Mesh Weld(this Mesh mesh, double angleToleranceRadians)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, angleToleranceRadians);
        }

        /// <summary>
        /// Makes sure that faces sharing an edge and having a difference of normal greater
        /// than or equal to angleToleranceRadians share vertexes along that edge, vertex normals
        /// are averaged.
        /// </summary>
        /// <param name="angleToleranceRadians">Angle at which to weld vertices.</param>
        public static Mesh Weld(Remote<Mesh> mesh, double angleToleranceRadians)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, angleToleranceRadians);
        }

        /// <summary>
        /// Removes mesh normals and reconstructs the face and vertex normals based
        /// on the orientation of the faces.
        /// </summary>
        public static Mesh RebuildNormals(this Mesh mesh)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Removes mesh normals and reconstructs the face and vertex normals based
        /// on the orientation of the faces.
        /// </summary>
        public static Mesh RebuildNormals(Remote<Mesh> mesh)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Extracts, or removes, non-manifold mesh edges. 
        /// </summary>
        /// <param name="selective">If true, then extract hanging faces only.</param>
        /// <returns>A mesh containing the extracted non-manifold parts if successful, null otherwise.</returns>
        public static Mesh ExtractNonManifoldEdges(this Mesh mesh, out Mesh updatedInstance, bool selective)
        {
            return ComputeServer.Post<Mesh, Mesh>(ApiAddress(), out updatedInstance, mesh, selective);
        }

        /// <summary>
        /// Extracts, or removes, non-manifold mesh edges. 
        /// </summary>
        /// <param name="selective">If true, then extract hanging faces only.</param>
        /// <returns>A mesh containing the extracted non-manifold parts if successful, null otherwise.</returns>
        public static Mesh ExtractNonManifoldEdges(Remote<Mesh> mesh, out Mesh updatedInstance, bool selective)
        {
            return ComputeServer.Post<Mesh, Mesh>(ApiAddress(), out updatedInstance, mesh, selective);
        }

        /// <summary>
        /// Attempts to "heal" naked edges in a mesh based on a given distance.  
        /// First attempts to move vertexes to neighboring vertexes that are within that
        /// distance away. Then it finds edges that have a closest point to the vertex within
        /// the distance and splits the edge. When it finds one it splits the edge and
        /// makes two new edges using that point.
        /// </summary>
        /// <param name="distance">Distance to not exceed when modifying the mesh.</param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool HealNakedEdges(this Mesh mesh, out Mesh updatedInstance, double distance)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, distance);
        }

        /// <summary>
        /// Attempts to "heal" naked edges in a mesh based on a given distance.  
        /// First attempts to move vertexes to neighboring vertexes that are within that
        /// distance away. Then it finds edges that have a closest point to the vertex within
        /// the distance and splits the edge. When it finds one it splits the edge and
        /// makes two new edges using that point.
        /// </summary>
        /// <param name="distance">Distance to not exceed when modifying the mesh.</param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool HealNakedEdges(Remote<Mesh> mesh, out Mesh updatedInstance, double distance)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, distance);
        }

        /// <summary>
        /// Attempts to determine "holes" in the mesh by chaining naked edges together. 
        /// Then it triangulates the closed polygons adds the faces to the mesh.
        /// </summary>
        /// <returns>true if successful, false otherwise.</returns>
        /// <remarks>This function does not differentiate between inner and outer naked edges.  
        /// If you need that, it would be better to use Mesh.FillHole.
        /// </remarks>
        public static bool FillHoles(this Mesh mesh, out Mesh updatedInstance)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh);
        }

        /// <summary>
        /// Attempts to determine "holes" in the mesh by chaining naked edges together. 
        /// Then it triangulates the closed polygons adds the faces to the mesh.
        /// </summary>
        /// <returns>true if successful, false otherwise.</returns>
        /// <remarks>This function does not differentiate between inner and outer naked edges.  
        /// If you need that, it would be better to use Mesh.FillHole.
        /// </remarks>
        public static bool FillHoles(Remote<Mesh> mesh, out Mesh updatedInstance)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh);
        }

        /// <summary>
        /// Given a starting "naked" edge index, this function attempts to determine a "hole"
        /// by chaining additional naked edges together until if returns to the start index.
        /// Then it triangulates the closed polygon and either adds the faces to the mesh.
        /// </summary>
        /// <param name="topologyEdgeIndex">Starting naked edge index.</param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool FileHole(this Mesh mesh, out Mesh updatedInstance, int topologyEdgeIndex)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, topologyEdgeIndex);
        }

        /// <summary>
        /// Given a starting "naked" edge index, this function attempts to determine a "hole"
        /// by chaining additional naked edges together until if returns to the start index.
        /// Then it triangulates the closed polygon and either adds the faces to the mesh.
        /// </summary>
        /// <param name="topologyEdgeIndex">Starting naked edge index.</param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool FileHole(Remote<Mesh> mesh, out Mesh updatedInstance, int topologyEdgeIndex)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, topologyEdgeIndex);
        }

        /// <summary>
        /// Attempts to fix inconsistencies in the directions of mesh faces in a mesh. This function
        /// does not modify mesh vertex normals, it rearranges the mesh face winding and face
        /// normals to make them all consistent. Note, you may want to call Mesh.Normals.ComputeNormals()
        /// to recompute vertex normals after calling this functions.
        /// </summary>
        /// <returns>number of faces that were modified.</returns>
        public static int UnifyNormals(this Mesh mesh, out Mesh updatedInstance)
        {
            return ComputeServer.Post<int, Mesh>(ApiAddress(), out updatedInstance, mesh);
        }

        /// <summary>
        /// Attempts to fix inconsistencies in the directions of mesh faces in a mesh. This function
        /// does not modify mesh vertex normals, it rearranges the mesh face winding and face
        /// normals to make them all consistent. Note, you may want to call Mesh.Normals.ComputeNormals()
        /// to recompute vertex normals after calling this functions.
        /// </summary>
        /// <returns>number of faces that were modified.</returns>
        public static int UnifyNormals(Remote<Mesh> mesh, out Mesh updatedInstance)
        {
            return ComputeServer.Post<int, Mesh>(ApiAddress(), out updatedInstance, mesh);
        }

        /// <summary>
        /// Attempts to fix inconsistencies in the directions of mesh faces in a mesh. This function
        /// does not modify mesh vertex normals, it rearranges the mesh face winding and face
        /// normals to make them all consistent. Note, you may want to call Mesh.Normals.ComputeNormals()
        /// to recompute vertex normals after calling this functions.
        /// </summary>
        /// <param name="countOnly">If true, then only the number of faces that would be modified is determined.</param>
        /// <returns>If countOnly=false, the number of faces that were modified. If countOnly=true, the number of faces that would be modified.</returns>
        public static int UnifyNormals(this Mesh mesh, out Mesh updatedInstance, bool countOnly)
        {
            return ComputeServer.Post<int, Mesh>(ApiAddress(), out updatedInstance, mesh, countOnly);
        }

        /// <summary>
        /// Attempts to fix inconsistencies in the directions of mesh faces in a mesh. This function
        /// does not modify mesh vertex normals, it rearranges the mesh face winding and face
        /// normals to make them all consistent. Note, you may want to call Mesh.Normals.ComputeNormals()
        /// to recompute vertex normals after calling this functions.
        /// </summary>
        /// <param name="countOnly">If true, then only the number of faces that would be modified is determined.</param>
        /// <returns>If countOnly=false, the number of faces that were modified. If countOnly=true, the number of faces that would be modified.</returns>
        public static int UnifyNormals(Remote<Mesh> mesh, out Mesh updatedInstance, bool countOnly)
        {
            return ComputeServer.Post<int, Mesh>(ApiAddress(), out updatedInstance, mesh, countOnly);
        }

        /// <summary>
        /// Splits up the mesh into its unconnected pieces.
        /// </summary>
        /// <returns>An array containing all the disjoint pieces that make up this Mesh.</returns>
        public static Mesh[] SplitDisjointPieces(this Mesh mesh)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Splits up the mesh into its unconnected pieces.
        /// </summary>
        /// <returns>An array containing all the disjoint pieces that make up this Mesh.</returns>
        public static Mesh[] SplitDisjointPieces(Remote<Mesh> mesh)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Split a mesh by an infinite plane.
        /// </summary>
        /// <param name="plane">The splitting plane.</param>
        /// <returns>A new mesh array with the split result. This can be null if no result was found.</returns>
        public static Mesh[] Split(this Mesh mesh, Plane plane)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh, plane);
        }

        /// <summary>
        /// Split a mesh by an infinite plane.
        /// </summary>
        /// <param name="plane">The splitting plane.</param>
        /// <returns>A new mesh array with the split result. This can be null if no result was found.</returns>
        public static Mesh[] Split(Remote<Mesh> mesh, Plane plane)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh, plane);
        }

        /// <summary>
        /// Split a mesh with another mesh. Suggestion: upgrade to overload with tolerance.
        /// </summary>
        /// <param name="mesh">Mesh to split with.</param>
        /// <returns>An array of mesh segments representing the split result.</returns>
        public static Mesh[] Split(this Mesh mesh, Mesh mesh)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh, mesh);
        }

        /// <summary>
        /// Split a mesh with another mesh. Suggestion: upgrade to overload with tolerance.
        /// </summary>
        /// <param name="mesh">Mesh to split with.</param>
        /// <returns>An array of mesh segments representing the split result.</returns>
        public static Mesh[] Split(Remote<Mesh> mesh, Remote<Mesh> mesh)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh, mesh);
        }

        /// <summary>
        /// Split a mesh with a collection of meshes. Suggestion: upgrade to overload with tolerance.
        /// Does not split at coplanar intersections.
        /// </summary>
        /// <param name="meshes">Meshes to split with.</param>
        /// <returns>An array of mesh segments representing the split result.</returns>
        public static Mesh[] Split(this Mesh mesh, IEnumerable<Mesh> meshes)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh, meshes);
        }

        /// <summary>
        /// Split a mesh with a collection of meshes. Suggestion: upgrade to overload with tolerance.
        /// Does not split at coplanar intersections.
        /// </summary>
        /// <param name="meshes">Meshes to split with.</param>
        /// <returns>An array of mesh segments representing the split result.</returns>
        public static Mesh[] Split(Remote<Mesh> mesh, Remote<IEnumerable<Mesh>> meshes)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh, meshes);
        }

        /// <summary>
        /// Split a mesh with a collection of meshes.
        /// </summary>
        /// <param name="meshes">Meshes to split with.</param>
        /// <param name="tolerance">A value for intersection tolerance.
        /// <para>WARNING! Correct values are typically in the (10e-8 - 10e-4) range.</para>
        /// <para>An option is to use the document tolerance diminished by a few orders or magnitude.</para>
        /// </param>
        /// <param name="splitAtCoplanar">If false, coplanar areas will not be separated.</param>
        /// <param name="textLog">A text log to write onto.</param>
        /// <param name="cancel">A cancellation token.</param>
        /// <param name="progress">A progress reporter item. This can be null.</param>
        /// <returns>An array of mesh parts representing the split result, or null: when no mesh intersected, or if a cancel stopped the computation.</returns>
        public static Mesh[] Split(this Mesh mesh, IEnumerable<Mesh> meshes, double tolerance, bool splitAtCoplanar, TextLog textLog, CancellationToken cancel, IProgress<double> progress)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh, meshes, tolerance, splitAtCoplanar, textLog, cancel, progress);
        }

        /// <summary>
        /// Split a mesh with a collection of meshes.
        /// </summary>
        /// <param name="meshes">Meshes to split with.</param>
        /// <param name="tolerance">A value for intersection tolerance.
        /// <para>WARNING! Correct values are typically in the (10e-8 - 10e-4) range.</para>
        /// <para>An option is to use the document tolerance diminished by a few orders or magnitude.</para>
        /// </param>
        /// <param name="splitAtCoplanar">If false, coplanar areas will not be separated.</param>
        /// <param name="textLog">A text log to write onto.</param>
        /// <param name="cancel">A cancellation token.</param>
        /// <param name="progress">A progress reporter item. This can be null.</param>
        /// <returns>An array of mesh parts representing the split result, or null: when no mesh intersected, or if a cancel stopped the computation.</returns>
        public static Mesh[] Split(Remote<Mesh> mesh, Remote<IEnumerable<Mesh>> meshes, double tolerance, bool splitAtCoplanar, TextLog textLog, CancellationToken cancel, IProgress<double> progress)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh, meshes, tolerance, splitAtCoplanar, textLog, cancel, progress);
        }

        /// <summary>
        /// Split a mesh with a collection of meshes.
        /// </summary>
        /// <param name="meshes">Meshes to split with.</param>
        /// <param name="tolerance">A value for intersection tolerance.
        /// <para>WARNING! Correct values are typically in the (10e-8 - 10e-4) range.</para>
        /// <para>An option is to use the document tolerance diminished by a few orders or magnitude.</para>
        /// </param>
        /// <param name="splitAtCoplanar">If false, coplanar areas will not be separated.</param>
        /// <param name="createNgons">If true, creates ngons along the split ridge.</param>
        /// <param name="textLog">A text log to write onto.</param>
        /// <param name="cancel">A cancellation token.</param>
        /// <param name="progress">A progress reporter item. This can be null.</param>
        /// <returns>An array of mesh parts representing the split result, or null: when no mesh intersected, or if a cancel stopped the computation.</returns>
        public static Mesh[] Split(this Mesh mesh, out Mesh updatedInstance, IEnumerable<Mesh> meshes, double tolerance, bool splitAtCoplanar, bool createNgons, TextLog textLog, CancellationToken cancel, IProgress<double> progress)
        {
            return ComputeServer.Post<Mesh[], Mesh>(ApiAddress(), out updatedInstance, mesh, meshes, tolerance, splitAtCoplanar, createNgons, textLog, cancel, progress);
        }

        /// <summary>
        /// Split a mesh with a collection of meshes.
        /// </summary>
        /// <param name="meshes">Meshes to split with.</param>
        /// <param name="tolerance">A value for intersection tolerance.
        /// <para>WARNING! Correct values are typically in the (10e-8 - 10e-4) range.</para>
        /// <para>An option is to use the document tolerance diminished by a few orders or magnitude.</para>
        /// </param>
        /// <param name="splitAtCoplanar">If false, coplanar areas will not be separated.</param>
        /// <param name="createNgons">If true, creates ngons along the split ridge.</param>
        /// <param name="textLog">A text log to write onto.</param>
        /// <param name="cancel">A cancellation token.</param>
        /// <param name="progress">A progress reporter item. This can be null.</param>
        /// <returns>An array of mesh parts representing the split result, or null: when no mesh intersected, or if a cancel stopped the computation.</returns>
        public static Mesh[] Split(Remote<Mesh> mesh, out Mesh updatedInstance, Remote<IEnumerable<Mesh>> meshes, double tolerance, bool splitAtCoplanar, bool createNgons, TextLog textLog, CancellationToken cancel, IProgress<double> progress)
        {
            return ComputeServer.Post<Mesh[], Mesh>(ApiAddress(), out updatedInstance, mesh, meshes, tolerance, splitAtCoplanar, createNgons, textLog, cancel, progress);
        }

        /// <summary>
        /// Constructs the outlines of a mesh projected against a plane.
        /// </summary>
        /// <param name="plane">A plane to project against.</param>
        /// <returns>An array of polylines, or null on error.</returns>
        public static Polyline[] GetOutlines(this Mesh mesh, Plane plane)
        {
            return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh, plane);
        }

        /// <summary>
        /// Constructs the outlines of a mesh projected against a plane.
        /// </summary>
        /// <param name="plane">A plane to project against.</param>
        /// <returns>An array of polylines, or null on error.</returns>
        public static Polyline[] GetOutlines(Remote<Mesh> mesh, Plane plane)
        {
            return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh, plane);
        }

        /// <summary>
        /// Constructs the outlines of a mesh. The projection information in the
        /// viewport is used to determine how the outlines are projected.
        /// </summary>
        /// <param name="viewport">A viewport to determine projection direction.</param>
        /// <returns>An array of polylines, or null on error.</returns>
        public static Polyline[] GetOutlines(this Mesh mesh, Display.RhinoViewport viewport)
        {
            return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh, viewport);
        }

        /// <summary>
        /// Constructs the outlines of a mesh. The projection information in the
        /// viewport is used to determine how the outlines are projected.
        /// </summary>
        /// <param name="viewport">A viewport to determine projection direction.</param>
        /// <returns>An array of polylines, or null on error.</returns>
        public static Polyline[] GetOutlines(Remote<Mesh> mesh, Display.RhinoViewport viewport)
        {
            return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh, viewport);
        }

        /// <summary>
        /// Constructs the outlines of a mesh.
        /// </summary>
        /// <param name="viewportInfo">The viewport info that provides the outline direction.</param>
        /// <param name="plane">Usually the view's construction plane. If a parallel projection and view plane is parallel to this, then project the results to the plane.</param>
        /// <returns>An array of polylines, or null on error.</returns>
        public static Polyline[] GetOutlines(this Mesh mesh, ViewportInfo viewportInfo, Plane plane)
        {
            return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh, viewportInfo, plane);
        }

        /// <summary>
        /// Constructs the outlines of a mesh.
        /// </summary>
        /// <param name="viewportInfo">The viewport info that provides the outline direction.</param>
        /// <param name="plane">Usually the view's construction plane. If a parallel projection and view plane is parallel to this, then project the results to the plane.</param>
        /// <returns>An array of polylines, or null on error.</returns>
        public static Polyline[] GetOutlines(Remote<Mesh> mesh, ViewportInfo viewportInfo, Plane plane)
        {
            return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh, viewportInfo, plane);
        }

        /// <summary>
        /// Returns all edges of a mesh that are considered "naked" in the
        /// sense that the edge only has one face.
        /// </summary>
        /// <returns>An array of polylines, or null on error.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_dupmeshboundary.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_dupmeshboundary.cs' lang='cs'/>
        /// <code source='examples\py\ex_dupmeshboundary.py' lang='py'/>
        /// </example>
        public static Polyline[] GetNakedEdges(this Mesh mesh)
        {
            return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Returns all edges of a mesh that are considered "naked" in the
        /// sense that the edge only has one face.
        /// </summary>
        /// <returns>An array of polylines, or null on error.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_dupmeshboundary.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_dupmeshboundary.cs' lang='cs'/>
        /// <code source='examples\py\ex_dupmeshboundary.py' lang='py'/>
        /// </example>
        public static Polyline[] GetNakedEdges(Remote<Mesh> mesh)
        {
            return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Explode the mesh into sub-meshes where a sub-mesh is a collection of faces that are contained
        /// within a closed loop of "unwelded" edges. Unwelded edges are edges where the faces that share
        /// the edge have unique mesh vertexes (not mesh topology vertexes) at both ends of the edge.
        /// </summary>
        /// <returns>
        /// Array of sub-meshes on success; null on error. If the count in the returned array is 1, then
        /// nothing happened and the output is essentially a copy of the input.
        /// </returns>
        public static Mesh[] ExplodeAtUnweldedEdges(this Mesh mesh)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Explode the mesh into sub-meshes where a sub-mesh is a collection of faces that are contained
        /// within a closed loop of "unwelded" edges. Unwelded edges are edges where the faces that share
        /// the edge have unique mesh vertexes (not mesh topology vertexes) at both ends of the edge.
        /// </summary>
        /// <returns>
        /// Array of sub-meshes on success; null on error. If the count in the returned array is 1, then
        /// nothing happened and the output is essentially a copy of the input.
        /// </returns>
        public static Mesh[] ExplodeAtUnweldedEdges(Remote<Mesh> mesh)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Gets the point on the mesh that is closest to a given test point.
        /// </summary>
        /// <param name="testPoint">Point to search for.</param>
        /// <returns>The point on the mesh closest to testPoint, or Point3d.Unset on failure.</returns>
        public static Point3d ClosestPoint(this Mesh mesh, Point3d testPoint)
        {
            return ComputeServer.Post<Point3d>(ApiAddress(), mesh, testPoint);
        }

        /// <summary>
        /// Gets the point on the mesh that is closest to a given test point.
        /// </summary>
        /// <param name="testPoint">Point to search for.</param>
        /// <returns>The point on the mesh closest to testPoint, or Point3d.Unset on failure.</returns>
        public static Point3d ClosestPoint(Remote<Mesh> mesh, Point3d testPoint)
        {
            return ComputeServer.Post<Point3d>(ApiAddress(), mesh, testPoint);
        }

        /// <summary>
        /// Gets the point on the mesh that is closest to a given test point. Similar to the 
        /// ClosestPoint function except this returns a MeshPoint class which includes
        /// extra information beyond just the location of the closest point.
        /// </summary>
        /// <param name="testPoint">The source of the search.</param>
        /// <param name="maximumDistance">
        /// Optional upper bound on the distance from test point to the mesh. 
        /// If you are only interested in finding a point Q on the mesh when 
        /// testPoint.DistanceTo(Q) &lt; maximumDistance, 
        /// then set maximumDistance to that value. 
        /// This parameter is ignored if you pass 0.0 for a maximumDistance.
        /// </param>
        /// <returns>closest point information on success. null on failure.</returns>
        public static MeshPoint ClosestMeshPoint(this Mesh mesh, Point3d testPoint, double maximumDistance)
        {
            return ComputeServer.Post<MeshPoint>(ApiAddress(), mesh, testPoint, maximumDistance);
        }

        /// <summary>
        /// Gets the point on the mesh that is closest to a given test point. Similar to the 
        /// ClosestPoint function except this returns a MeshPoint class which includes
        /// extra information beyond just the location of the closest point.
        /// </summary>
        /// <param name="testPoint">The source of the search.</param>
        /// <param name="maximumDistance">
        /// Optional upper bound on the distance from test point to the mesh. 
        /// If you are only interested in finding a point Q on the mesh when 
        /// testPoint.DistanceTo(Q) &lt; maximumDistance, 
        /// then set maximumDistance to that value. 
        /// This parameter is ignored if you pass 0.0 for a maximumDistance.
        /// </param>
        /// <returns>closest point information on success. null on failure.</returns>
        public static MeshPoint ClosestMeshPoint(Remote<Mesh> mesh, Point3d testPoint, double maximumDistance)
        {
            return ComputeServer.Post<MeshPoint>(ApiAddress(), mesh, testPoint, maximumDistance);
        }

        /// <summary>
        /// Gets the point on the mesh that is closest to a given test point.
        /// </summary>
        /// <param name="testPoint">Point to search for.</param>
        /// <param name="pointOnMesh">Point on the mesh closest to testPoint.</param>
        /// <param name="maximumDistance">
        /// Optional upper bound on the distance from test point to the mesh. 
        /// If you are only interested in finding a point Q on the mesh when 
        /// testPoint.DistanceTo(Q) &lt; maximumDistance, 
        /// then set maximumDistance to that value. 
        /// This parameter is ignored if you pass 0.0 for a maximumDistance.
        /// </param>
        /// <returns>
        /// Index of face that the closest point lies on if successful. 
        /// -1 if not successful; the value of pointOnMesh is undefined.
        /// </returns>
        public static int ClosestPoint(this Mesh mesh, Point3d testPoint, out Point3d pointOnMesh, double maximumDistance)
        {
            return ComputeServer.Post<int, Point3d>(ApiAddress(), out pointOnMesh, mesh, testPoint, maximumDistance);
        }

        /// <summary>
        /// Gets the point on the mesh that is closest to a given test point.
        /// </summary>
        /// <param name="testPoint">Point to search for.</param>
        /// <param name="pointOnMesh">Point on the mesh closest to testPoint.</param>
        /// <param name="maximumDistance">
        /// Optional upper bound on the distance from test point to the mesh. 
        /// If you are only interested in finding a point Q on the mesh when 
        /// testPoint.DistanceTo(Q) &lt; maximumDistance, 
        /// then set maximumDistance to that value. 
        /// This parameter is ignored if you pass 0.0 for a maximumDistance.
        /// </param>
        /// <returns>
        /// Index of face that the closest point lies on if successful. 
        /// -1 if not successful; the value of pointOnMesh is undefined.
        /// </returns>
        public static int ClosestPoint(Remote<Mesh> mesh, Point3d testPoint, out Point3d pointOnMesh, double maximumDistance)
        {
            return ComputeServer.Post<int, Point3d>(ApiAddress(), out pointOnMesh, mesh, testPoint, maximumDistance);
        }

        /// <summary>
        /// Gets the point on the mesh that is closest to a given test point.
        /// </summary>
        /// <param name="testPoint">Point to search for.</param>
        /// <param name="pointOnMesh">Point on the mesh closest to testPoint.</param>
        /// <param name="normalAtPoint">The normal vector of the mesh at the closest point.</param>
        /// <param name="maximumDistance">
        /// Optional upper bound on the distance from test point to the mesh. 
        /// If you are only interested in finding a point Q on the mesh when 
        /// testPoint.DistanceTo(Q) &lt; maximumDistance, 
        /// then set maximumDistance to that value. 
        /// This parameter is ignored if you pass 0.0 for a maximumDistance.
        /// </param>
        /// <returns>
        /// Index of face that the closest point lies on if successful. 
        /// -1 if not successful; the value of pointOnMesh is undefined.
        /// </returns>
        public static int ClosestPoint(this Mesh mesh, Point3d testPoint, out Point3d pointOnMesh, out Vector3d normalAtPoint, double maximumDistance)
        {
            return ComputeServer.Post<int, Point3d, Vector3d>(ApiAddress(), out pointOnMesh, out normalAtPoint, mesh, testPoint, maximumDistance);
        }

        /// <summary>
        /// Gets the point on the mesh that is closest to a given test point.
        /// </summary>
        /// <param name="testPoint">Point to search for.</param>
        /// <param name="pointOnMesh">Point on the mesh closest to testPoint.</param>
        /// <param name="normalAtPoint">The normal vector of the mesh at the closest point.</param>
        /// <param name="maximumDistance">
        /// Optional upper bound on the distance from test point to the mesh. 
        /// If you are only interested in finding a point Q on the mesh when 
        /// testPoint.DistanceTo(Q) &lt; maximumDistance, 
        /// then set maximumDistance to that value. 
        /// This parameter is ignored if you pass 0.0 for a maximumDistance.
        /// </param>
        /// <returns>
        /// Index of face that the closest point lies on if successful. 
        /// -1 if not successful; the value of pointOnMesh is undefined.
        /// </returns>
        public static int ClosestPoint(Remote<Mesh> mesh, Point3d testPoint, out Point3d pointOnMesh, out Vector3d normalAtPoint, double maximumDistance)
        {
            return ComputeServer.Post<int, Point3d, Vector3d>(ApiAddress(), out pointOnMesh, out normalAtPoint, mesh, testPoint, maximumDistance);
        }

        /// <summary>
        /// Evaluate a mesh at a set of barycentric coordinates.
        /// </summary>
        /// <param name="meshPoint">MeshPoint instance containing a valid Face Index and Barycentric coordinates.</param>
        /// <returns>A Point on the mesh or Point3d.Unset if the faceIndex is not valid or if the barycentric coordinates could not be evaluated.</returns>
        public static Point3d PointAt(this Mesh mesh, MeshPoint meshPoint)
        {
            return ComputeServer.Post<Point3d>(ApiAddress(), mesh, meshPoint);
        }

        /// <summary>
        /// Evaluate a mesh at a set of barycentric coordinates.
        /// </summary>
        /// <param name="meshPoint">MeshPoint instance containing a valid Face Index and Barycentric coordinates.</param>
        /// <returns>A Point on the mesh or Point3d.Unset if the faceIndex is not valid or if the barycentric coordinates could not be evaluated.</returns>
        public static Point3d PointAt(Remote<Mesh> mesh, MeshPoint meshPoint)
        {
            return ComputeServer.Post<Point3d>(ApiAddress(), mesh, meshPoint);
        }

        /// <summary>
        /// Evaluates a mesh at a set of barycentric coordinates. Barycentric coordinates must 
        /// be assigned in accordance with the rules as defined by MeshPoint.T.
        /// </summary>
        /// <param name="faceIndex">Index of triangle or quad to evaluate.</param>
        /// <param name="t0">First barycentric coordinate.</param>
        /// <param name="t1">Second barycentric coordinate.</param>
        /// <param name="t2">Third barycentric coordinate.</param>
        /// <param name="t3">Fourth barycentric coordinate. If the face is a triangle, this coordinate will be ignored.</param>
        /// <returns>A Point on the mesh or Point3d.Unset if the faceIndex is not valid or if the barycentric coordinates could not be evaluated.</returns>
        public static Point3d PointAt(this Mesh mesh, int faceIndex, double t0, double t1, double t2, double t3)
        {
            return ComputeServer.Post<Point3d>(ApiAddress(), mesh, faceIndex, t0, t1, t2, t3);
        }

        /// <summary>
        /// Evaluates a mesh at a set of barycentric coordinates. Barycentric coordinates must 
        /// be assigned in accordance with the rules as defined by MeshPoint.T.
        /// </summary>
        /// <param name="faceIndex">Index of triangle or quad to evaluate.</param>
        /// <param name="t0">First barycentric coordinate.</param>
        /// <param name="t1">Second barycentric coordinate.</param>
        /// <param name="t2">Third barycentric coordinate.</param>
        /// <param name="t3">Fourth barycentric coordinate. If the face is a triangle, this coordinate will be ignored.</param>
        /// <returns>A Point on the mesh or Point3d.Unset if the faceIndex is not valid or if the barycentric coordinates could not be evaluated.</returns>
        public static Point3d PointAt(Remote<Mesh> mesh, int faceIndex, double t0, double t1, double t2, double t3)
        {
            return ComputeServer.Post<Point3d>(ApiAddress(), mesh, faceIndex, t0, t1, t2, t3);
        }

        /// <summary>
        /// Evaluate a mesh normal at a set of barycentric coordinates.
        /// </summary>
        /// <param name="meshPoint">MeshPoint instance containing a valid Face Index and Barycentric coordinates.</param>
        /// <returns>A Normal vector to the mesh or Vector3d.Unset if the faceIndex is not valid or if the barycentric coordinates could not be evaluated.</returns>
        public static Vector3d NormalAt(this Mesh mesh, MeshPoint meshPoint)
        {
            return ComputeServer.Post<Vector3d>(ApiAddress(), mesh, meshPoint);
        }

        /// <summary>
        /// Evaluate a mesh normal at a set of barycentric coordinates.
        /// </summary>
        /// <param name="meshPoint">MeshPoint instance containing a valid Face Index and Barycentric coordinates.</param>
        /// <returns>A Normal vector to the mesh or Vector3d.Unset if the faceIndex is not valid or if the barycentric coordinates could not be evaluated.</returns>
        public static Vector3d NormalAt(Remote<Mesh> mesh, MeshPoint meshPoint)
        {
            return ComputeServer.Post<Vector3d>(ApiAddress(), mesh, meshPoint);
        }

        /// <summary>
        /// Evaluate a mesh normal at a set of barycentric coordinates. Barycentric coordinates must 
        /// be assigned in accordance with the rules as defined by MeshPoint.T.
        /// </summary>
        /// <param name="faceIndex">Index of triangle or quad to evaluate.</param>
        /// <param name="t0">First barycentric coordinate.</param>
        /// <param name="t1">Second barycentric coordinate.</param>
        /// <param name="t2">Third barycentric coordinate.</param>
        /// <param name="t3">Fourth barycentric coordinate. If the face is a triangle, this coordinate will be ignored.</param>
        /// <returns>A Normal vector to the mesh or Vector3d.Unset if the faceIndex is not valid or if the barycentric coordinates could not be evaluated.</returns>
        public static Vector3d NormalAt(this Mesh mesh, int faceIndex, double t0, double t1, double t2, double t3)
        {
            return ComputeServer.Post<Vector3d>(ApiAddress(), mesh, faceIndex, t0, t1, t2, t3);
        }

        /// <summary>
        /// Evaluate a mesh normal at a set of barycentric coordinates. Barycentric coordinates must 
        /// be assigned in accordance with the rules as defined by MeshPoint.T.
        /// </summary>
        /// <param name="faceIndex">Index of triangle or quad to evaluate.</param>
        /// <param name="t0">First barycentric coordinate.</param>
        /// <param name="t1">Second barycentric coordinate.</param>
        /// <param name="t2">Third barycentric coordinate.</param>
        /// <param name="t3">Fourth barycentric coordinate. If the face is a triangle, this coordinate will be ignored.</param>
        /// <returns>A Normal vector to the mesh or Vector3d.Unset if the faceIndex is not valid or if the barycentric coordinates could not be evaluated.</returns>
        public static Vector3d NormalAt(Remote<Mesh> mesh, int faceIndex, double t0, double t1, double t2, double t3)
        {
            return ComputeServer.Post<Vector3d>(ApiAddress(), mesh, faceIndex, t0, t1, t2, t3);
        }

        /// <summary>
        /// Evaluate a mesh color at a set of barycentric coordinates.
        /// </summary>
        /// <param name="meshPoint">MeshPoint instance containing a valid Face Index and Barycentric coordinates.</param>
        /// <returns>The interpolated vertex color on the mesh or Color.Transparent if the faceIndex is not valid, 
        /// if the barycentric coordinates could not be evaluated, or if there are no colors defined on the mesh.</returns>
        public static Color ColorAt(this Mesh mesh, MeshPoint meshPoint)
        {
            return ComputeServer.Post<Color>(ApiAddress(), mesh, meshPoint);
        }

        /// <summary>
        /// Evaluate a mesh color at a set of barycentric coordinates.
        /// </summary>
        /// <param name="meshPoint">MeshPoint instance containing a valid Face Index and Barycentric coordinates.</param>
        /// <returns>The interpolated vertex color on the mesh or Color.Transparent if the faceIndex is not valid, 
        /// if the barycentric coordinates could not be evaluated, or if there are no colors defined on the mesh.</returns>
        public static Color ColorAt(Remote<Mesh> mesh, MeshPoint meshPoint)
        {
            return ComputeServer.Post<Color>(ApiAddress(), mesh, meshPoint);
        }

        /// <summary>
        /// Evaluate a mesh normal at a set of barycentric coordinates. Barycentric coordinates must 
        /// be assigned in accordance with the rules as defined by <see cref="MeshPoint.T">MeshPoint.T</see>.
        /// </summary>
        /// <param name="faceIndex">Index of triangle or quad to evaluate.</param>
        /// <param name="t0">First barycentric coordinate.</param>
        /// <param name="t1">Second barycentric coordinate.</param>
        /// <param name="t2">Third barycentric coordinate.</param>
        /// <param name="t3">Fourth barycentric coordinate. If the face is a triangle, this coordinate will be ignored.</param>
        /// <returns>The interpolated vertex color on the mesh or Color.Transparent if the faceIndex is not valid, 
        /// if the barycentric coordinates could not be evaluated, or if there are no colors defined on the mesh.</returns>
        /// <remarks>Coordinate 0,0,0,0 is not a valid set of barycentric coordinates. The sum of t0 to t3 should be 1.</remarks>
        public static Color ColorAt(this Mesh mesh, int faceIndex, double t0, double t1, double t2, double t3)
        {
            return ComputeServer.Post<Color>(ApiAddress(), mesh, faceIndex, t0, t1, t2, t3);
        }

        /// <summary>
        /// Evaluate a mesh normal at a set of barycentric coordinates. Barycentric coordinates must 
        /// be assigned in accordance with the rules as defined by <see cref="MeshPoint.T">MeshPoint.T</see>.
        /// </summary>
        /// <param name="faceIndex">Index of triangle or quad to evaluate.</param>
        /// <param name="t0">First barycentric coordinate.</param>
        /// <param name="t1">Second barycentric coordinate.</param>
        /// <param name="t2">Third barycentric coordinate.</param>
        /// <param name="t3">Fourth barycentric coordinate. If the face is a triangle, this coordinate will be ignored.</param>
        /// <returns>The interpolated vertex color on the mesh or Color.Transparent if the faceIndex is not valid, 
        /// if the barycentric coordinates could not be evaluated, or if there are no colors defined on the mesh.</returns>
        /// <remarks>Coordinate 0,0,0,0 is not a valid set of barycentric coordinates. The sum of t0 to t3 should be 1.</remarks>
        public static Color ColorAt(Remote<Mesh> mesh, int faceIndex, double t0, double t1, double t2, double t3)
        {
            return ComputeServer.Post<Color>(ApiAddress(), mesh, faceIndex, t0, t1, t2, t3);
        }

        /// <summary>
        /// Pulls a collection of points to a mesh.
        /// </summary>
        /// <param name="points">An array, a list or any enumerable set of points.</param>
        /// <returns>An array of points. This can be empty.</returns>
        public static Point3d[] PullPointsToMesh(this Mesh mesh, IEnumerable<Point3d> points)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), mesh, points);
        }

        /// <summary>
        /// Pulls a collection of points to a mesh.
        /// </summary>
        /// <param name="points">An array, a list or any enumerable set of points.</param>
        /// <returns>An array of points. This can be empty.</returns>
        public static Point3d[] PullPointsToMesh(Remote<Mesh> mesh, IEnumerable<Point3d> points)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), mesh, points);
        }

        /// <summary>
        /// Gets a polyline approximation of the input curve and then moves its control points to the closest point on the mesh.
        /// Then it "connects the points" over edges so that a polyline on the mesh is formed.
        /// </summary>
        /// <param name="curve">A curve to pull.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>A polyline curve, or null if none could be constructed.</returns>
        public static PolylineCurve PullCurve(this Mesh mesh, Curve curve, double tolerance)
        {
            return ComputeServer.Post<PolylineCurve>(ApiAddress(), mesh, curve, tolerance);
        }

        /// <summary>
        /// Gets a polyline approximation of the input curve and then moves its control points to the closest point on the mesh.
        /// Then it "connects the points" over edges so that a polyline on the mesh is formed.
        /// </summary>
        /// <param name="curve">A curve to pull.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>A polyline curve, or null if none could be constructed.</returns>
        public static PolylineCurve PullCurve(Remote<Mesh> mesh, Remote<Curve> curve, double tolerance)
        {
            return ComputeServer.Post<PolylineCurve>(ApiAddress(), mesh, curve, tolerance);
        }

        /// <summary>
        /// Splits a mesh by adding edges in correspondence with input polylines, and divides the mesh at partitioned areas.
        /// Polyline segments that are measured not to be on the mesh will be ignored.
        /// </summary>
        /// <param name="curves">An array, a list or any enumerable of polyline curves.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>An array of meshes, or null if no change would happen.</returns>
        public static Mesh[] SplitWithProjectedPolylines(this Mesh mesh, IEnumerable<PolylineCurve> curves, double tolerance)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh, curves, tolerance);
        }

        /// <summary>
        /// Splits a mesh by adding edges in correspondence with input polylines, and divides the mesh at partitioned areas.
        /// Polyline segments that are measured not to be on the mesh will be ignored.
        /// </summary>
        /// <param name="curves">An array, a list or any enumerable of polyline curves.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <returns>An array of meshes, or null if no change would happen.</returns>
        public static Mesh[] SplitWithProjectedPolylines(Remote<Mesh> mesh, IEnumerable<PolylineCurve> curves, double tolerance)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh, curves, tolerance);
        }

        /// <summary>
        /// Splits a mesh by adding edges in correspondence with input polylines, and divides the mesh at partitioned areas.
        /// Polyline segments that are measured not to be on the mesh will be ignored.
        /// </summary>
        /// <param name="curves">An array, a list or any enumerable of polyline curves.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <param name="textLog">A text log, or null.</param>
        /// <param name="cancel">A cancellation token to stop the computation at a given point.</param>
        /// <param name="progress">A progress reporter to inform the user about progress. The reported value is indicative.</param>
        /// <returns>An array of meshes, or null if no change would happen.</returns>
        public static Mesh[] SplitWithProjectedPolylines(this Mesh mesh, IEnumerable<PolylineCurve> curves, double tolerance, TextLog textLog, CancellationToken cancel, IProgress<double> progress)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh, curves, tolerance, textLog, cancel, progress);
        }

        /// <summary>
        /// Splits a mesh by adding edges in correspondence with input polylines, and divides the mesh at partitioned areas.
        /// Polyline segments that are measured not to be on the mesh will be ignored.
        /// </summary>
        /// <param name="curves">An array, a list or any enumerable of polyline curves.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <param name="textLog">A text log, or null.</param>
        /// <param name="cancel">A cancellation token to stop the computation at a given point.</param>
        /// <param name="progress">A progress reporter to inform the user about progress. The reported value is indicative.</param>
        /// <returns>An array of meshes, or null if no change would happen.</returns>
        public static Mesh[] SplitWithProjectedPolylines(Remote<Mesh> mesh, IEnumerable<PolylineCurve> curves, double tolerance, TextLog textLog, CancellationToken cancel, IProgress<double> progress)
        {
            return ComputeServer.Post<Mesh[]>(ApiAddress(), mesh, curves, tolerance, textLog, cancel, progress);
        }

        /// <summary>
        /// Makes a new mesh with vertices offset a distance in the opposite direction of the existing vertex normals.
        /// Same as Mesh.Offset(distance, false)
        /// </summary>
        /// <param name="distance">A distance value to use for offsetting.</param>
        /// <returns>A new mesh on success, or null on failure.</returns>
        public static Mesh Offset(this Mesh mesh, double distance)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, distance);
        }

        /// <summary>
        /// Makes a new mesh with vertices offset a distance in the opposite direction of the existing vertex normals.
        /// Same as Mesh.Offset(distance, false)
        /// </summary>
        /// <param name="distance">A distance value to use for offsetting.</param>
        /// <returns>A new mesh on success, or null on failure.</returns>
        public static Mesh Offset(Remote<Mesh> mesh, double distance)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, distance);
        }

        /// <summary>
        /// Makes a new mesh with vertices offset a distance in the opposite direction of the existing vertex normals.
        /// Optionally, based on the value of solidify, adds the input mesh and a ribbon of faces along any naked edges.
        /// If solidify is false it acts exactly as the Offset(distance) function.
        /// </summary>
        /// <param name="distance">A distance value.</param>
        /// <param name="solidify">true if the mesh should be solidified.</param>
        /// <returns>A new mesh on success, or null on failure.</returns>
        public static Mesh Offset(this Mesh mesh, double distance, bool solidify)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, distance, solidify);
        }

        /// <summary>
        /// Makes a new mesh with vertices offset a distance in the opposite direction of the existing vertex normals.
        /// Optionally, based on the value of solidify, adds the input mesh and a ribbon of faces along any naked edges.
        /// If solidify is false it acts exactly as the Offset(distance) function.
        /// </summary>
        /// <param name="distance">A distance value.</param>
        /// <param name="solidify">true if the mesh should be solidified.</param>
        /// <returns>A new mesh on success, or null on failure.</returns>
        public static Mesh Offset(Remote<Mesh> mesh, double distance, bool solidify)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, distance, solidify);
        }

        /// <summary>
        /// Makes a new mesh with vertices offset a distance along the direction parameter.
        /// Optionally, based on the value of solidify, adds the input mesh and a ribbon of faces along any naked edges.
        /// If solidify is false it acts exactly as the Offset(distance) function.
        /// </summary>
        /// <param name="distance">A distance value.</param>
        /// <param name="solidify">true if the mesh should be solidified.</param>
        /// <param name="direction">Direction of offset for all vertices.</param>
        /// <returns>A new mesh on success, or null on failure.</returns>
        public static Mesh Offset(this Mesh mesh, double distance, bool solidify, Vector3d direction)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, distance, solidify, direction);
        }

        /// <summary>
        /// Makes a new mesh with vertices offset a distance along the direction parameter.
        /// Optionally, based on the value of solidify, adds the input mesh and a ribbon of faces along any naked edges.
        /// If solidify is false it acts exactly as the Offset(distance) function.
        /// </summary>
        /// <param name="distance">A distance value.</param>
        /// <param name="solidify">true if the mesh should be solidified.</param>
        /// <param name="direction">Direction of offset for all vertices.</param>
        /// <returns>A new mesh on success, or null on failure.</returns>
        public static Mesh Offset(Remote<Mesh> mesh, double distance, bool solidify, Vector3d direction)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, distance, solidify, direction);
        }

        /// <summary>
        /// Makes a new mesh with vertices offset a distance along the direction parameter.
        /// Optionally, based on the value of solidify, adds the input mesh and a ribbon of faces along any naked edges.
        /// If solidify is false it acts exactly as the Offset(distance) function. Returns list of wall faces, i.e. the
        /// faces that connect original and offset mesh when solidified.
        /// </summary>
        /// <param name="distance">A distance value.</param>
        /// <param name="solidify">true if the mesh should be solidified.</param>
        /// <param name="direction">Direction of offset for all vertices.</param>
        /// <param name="wallFacesOut">Returns list of wall faces.</param>
        /// <returns>A new mesh on success, or null on failure.</returns>
        public static Mesh Offset(this Mesh mesh, double distance, bool solidify, Vector3d direction, out List<int> wallFacesOut)
        {
            return ComputeServer.Post<Mesh, List<int>>(ApiAddress(), out wallFacesOut, mesh, distance, solidify, direction);
        }

        /// <summary>
        /// Makes a new mesh with vertices offset a distance along the direction parameter.
        /// Optionally, based on the value of solidify, adds the input mesh and a ribbon of faces along any naked edges.
        /// If solidify is false it acts exactly as the Offset(distance) function. Returns list of wall faces, i.e. the
        /// faces that connect original and offset mesh when solidified.
        /// </summary>
        /// <param name="distance">A distance value.</param>
        /// <param name="solidify">true if the mesh should be solidified.</param>
        /// <param name="direction">Direction of offset for all vertices.</param>
        /// <param name="wallFacesOut">Returns list of wall faces.</param>
        /// <returns>A new mesh on success, or null on failure.</returns>
        public static Mesh Offset(Remote<Mesh> mesh, double distance, bool solidify, Vector3d direction, out List<int> wallFacesOut)
        {
            return ComputeServer.Post<Mesh, List<int>>(ApiAddress(), out wallFacesOut, mesh, distance, solidify, direction);
        }

        /// <summary>
        /// Collapses multiple mesh faces, with greater/less than edge length, based on the principles 
        /// found in Stan Melax's mesh reduction PDF, 
        /// see http://pomax.nihongoresources.com/downloads/PolygonReduction.pdf
        /// </summary>
        /// <param name="bGreaterThan">Determines whether edge with lengths greater than or less than edgeLength are collapsed.</param>
        /// <param name="edgeLength">Length with which to compare to edge lengths.</param>
        /// <returns>Number of edges (faces) that were collapsed.</returns>
        /// <remarks>
        /// This number may differ from the initial number of edges that meet
        /// the input criteria because the lengths of some initial edges may be altered as other edges are collapsed.
        /// </remarks>
        public static int CollapseFacesByEdgeLength(this Mesh mesh, out Mesh updatedInstance, bool bGreaterThan, double edgeLength)
        {
            return ComputeServer.Post<int, Mesh>(ApiAddress(), out updatedInstance, mesh, bGreaterThan, edgeLength);
        }

        /// <summary>
        /// Collapses multiple mesh faces, with greater/less than edge length, based on the principles 
        /// found in Stan Melax's mesh reduction PDF, 
        /// see http://pomax.nihongoresources.com/downloads/PolygonReduction.pdf
        /// </summary>
        /// <param name="bGreaterThan">Determines whether edge with lengths greater than or less than edgeLength are collapsed.</param>
        /// <param name="edgeLength">Length with which to compare to edge lengths.</param>
        /// <returns>Number of edges (faces) that were collapsed.</returns>
        /// <remarks>
        /// This number may differ from the initial number of edges that meet
        /// the input criteria because the lengths of some initial edges may be altered as other edges are collapsed.
        /// </remarks>
        public static int CollapseFacesByEdgeLength(Remote<Mesh> mesh, out Mesh updatedInstance, bool bGreaterThan, double edgeLength)
        {
            return ComputeServer.Post<int, Mesh>(ApiAddress(), out updatedInstance, mesh, bGreaterThan, edgeLength);
        }

        /// <summary>
        /// Collapses multiple mesh faces, with areas less than LessThanArea and greater than GreaterThanArea, 
        /// based on the principles found in Stan Melax's mesh reduction PDF, 
        /// see http://pomax.nihongoresources.com/downloads/PolygonReduction.pdf
        /// </summary>
        /// <param name="lessThanArea">Area in which faces are selected if their area is less than or equal to.</param>
        /// <param name="greaterThanArea">Area in which faces are selected if their area is greater than or equal to.</param>
        /// <returns>Number of faces that were collapsed in the process.</returns>
        /// <remarks>
        /// This number may differ from the initial number of faces that meet
        /// the input criteria because the areas of some initial faces may be altered as other faces are collapsed.
        /// The face area must be both less than LessThanArea AND greater than GreaterThanArea in order to be considered.  
        /// Use large numbers for lessThanArea or zero for greaterThanArea to simulate an OR.
        /// </remarks>
        public static int CollapseFacesByArea(this Mesh mesh, out Mesh updatedInstance, double lessThanArea, double greaterThanArea)
        {
            return ComputeServer.Post<int, Mesh>(ApiAddress(), out updatedInstance, mesh, lessThanArea, greaterThanArea);
        }

        /// <summary>
        /// Collapses multiple mesh faces, with areas less than LessThanArea and greater than GreaterThanArea, 
        /// based on the principles found in Stan Melax's mesh reduction PDF, 
        /// see http://pomax.nihongoresources.com/downloads/PolygonReduction.pdf
        /// </summary>
        /// <param name="lessThanArea">Area in which faces are selected if their area is less than or equal to.</param>
        /// <param name="greaterThanArea">Area in which faces are selected if their area is greater than or equal to.</param>
        /// <returns>Number of faces that were collapsed in the process.</returns>
        /// <remarks>
        /// This number may differ from the initial number of faces that meet
        /// the input criteria because the areas of some initial faces may be altered as other faces are collapsed.
        /// The face area must be both less than LessThanArea AND greater than GreaterThanArea in order to be considered.  
        /// Use large numbers for lessThanArea or zero for greaterThanArea to simulate an OR.
        /// </remarks>
        public static int CollapseFacesByArea(Remote<Mesh> mesh, out Mesh updatedInstance, double lessThanArea, double greaterThanArea)
        {
            return ComputeServer.Post<int, Mesh>(ApiAddress(), out updatedInstance, mesh, lessThanArea, greaterThanArea);
        }

        /// <summary>
        /// Collapses a multiple mesh faces, determined by face aspect ratio, based on criteria found in Stan Melax's polygon reduction,
        /// see http://pomax.nihongoresources.com/downloads/PolygonReduction.pdf
        /// </summary>
        /// <param name="aspectRatio">Faces with an aspect ratio less than aspectRatio are considered as candidates.</param>
        /// <returns>Number of faces that were collapsed in the process.</returns>
        /// <remarks>
        /// This number may differ from the initial number of faces that meet 
        /// the input criteria because the aspect ratios of some initial faces may be altered as other faces are collapsed.
        /// </remarks>
        public static int CollapseFacesByByAspectRatio(this Mesh mesh, out Mesh updatedInstance, double aspectRatio)
        {
            return ComputeServer.Post<int, Mesh>(ApiAddress(), out updatedInstance, mesh, aspectRatio);
        }

        /// <summary>
        /// Collapses a multiple mesh faces, determined by face aspect ratio, based on criteria found in Stan Melax's polygon reduction,
        /// see http://pomax.nihongoresources.com/downloads/PolygonReduction.pdf
        /// </summary>
        /// <param name="aspectRatio">Faces with an aspect ratio less than aspectRatio are considered as candidates.</param>
        /// <returns>Number of faces that were collapsed in the process.</returns>
        /// <remarks>
        /// This number may differ from the initial number of faces that meet 
        /// the input criteria because the aspect ratios of some initial faces may be altered as other faces are collapsed.
        /// </remarks>
        public static int CollapseFacesByByAspectRatio(Remote<Mesh> mesh, out Mesh updatedInstance, double aspectRatio)
        {
            return ComputeServer.Post<int, Mesh>(ApiAddress(), out updatedInstance, mesh, aspectRatio);
        }

        /// <summary>
        /// Allows to obtain unsafe pointers to the underlying unmanaged data structures of the mesh.
        /// </summary>
        /// <param name="writable">true if user will need to write onto the structure. false otherwise.</param>
        /// <returns>A lock that needs to be released.</returns>
        /// <remarks>The lock implements the IDisposable interface, and one call of its
        /// <see cref="IDisposable.Dispose()"/> or <see cref="ReleaseUnsafeLock"/> will update the data structure as required.
        /// This can be achieved with a using statement (Using in Vb.Net).</remarks>
        public static MeshUnsafeLock GetUnsafeLock(this Mesh mesh, out Mesh updatedInstance, bool writable)
        {
            return ComputeServer.Post<MeshUnsafeLock, Mesh>(ApiAddress(), out updatedInstance, mesh, writable);
        }

        /// <summary>
        /// Allows to obtain unsafe pointers to the underlying unmanaged data structures of the mesh.
        /// </summary>
        /// <param name="writable">true if user will need to write onto the structure. false otherwise.</param>
        /// <returns>A lock that needs to be released.</returns>
        /// <remarks>The lock implements the IDisposable interface, and one call of its
        /// <see cref="IDisposable.Dispose()"/> or <see cref="ReleaseUnsafeLock"/> will update the data structure as required.
        /// This can be achieved with a using statement (Using in Vb.Net).</remarks>
        public static MeshUnsafeLock GetUnsafeLock(Remote<Mesh> mesh, out Mesh updatedInstance, bool writable)
        {
            return ComputeServer.Post<MeshUnsafeLock, Mesh>(ApiAddress(), out updatedInstance, mesh, writable);
        }

        /// <summary>
        /// Updates the Mesh data with the information that was stored via the <see cref="MeshUnsafeLock"/>.
        /// </summary>
        /// <param name="meshData">The data that will be unlocked.</param>
        public static Mesh ReleaseUnsafeLock(this Mesh mesh, MeshUnsafeLock meshData)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, meshData);
        }

        /// <summary>
        /// Updates the Mesh data with the information that was stored via the <see cref="MeshUnsafeLock"/>.
        /// </summary>
        /// <param name="meshData">The data that will be unlocked.</param>
        public static Mesh ReleaseUnsafeLock(Remote<Mesh> mesh, MeshUnsafeLock meshData)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, meshData);
        }

        /// <summary>
        /// Constructs new mesh from the current one, with shut lining applied to it.
        /// </summary>
        /// <param name="faceted">Specifies whether the shutline is faceted.</param>
        /// <param name="tolerance">The tolerance of the shutline.</param>
        /// <param name="curves">A collection of curve arguments.</param>
        /// <returns>A new mesh with shutlining. Null on failure.</returns>
        /// <exception cref="ArgumentNullException">If curves is null.</exception>
        /// <exception cref="InvalidOperationException">If displacement failed
        /// because of an error. The exception message specifies the error.</exception>
        public static Mesh WithShutLining(this Mesh mesh, bool faceted, double tolerance, IEnumerable<ShutLiningCurveInfo> curves)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, faceted, tolerance, curves);
        }

        /// <summary>
        /// Constructs new mesh from the current one, with shut lining applied to it.
        /// </summary>
        /// <param name="faceted">Specifies whether the shutline is faceted.</param>
        /// <param name="tolerance">The tolerance of the shutline.</param>
        /// <param name="curves">A collection of curve arguments.</param>
        /// <returns>A new mesh with shutlining. Null on failure.</returns>
        /// <exception cref="ArgumentNullException">If curves is null.</exception>
        /// <exception cref="InvalidOperationException">If displacement failed
        /// because of an error. The exception message specifies the error.</exception>
        public static Mesh WithShutLining(Remote<Mesh> mesh, bool faceted, double tolerance, IEnumerable<ShutLiningCurveInfo> curves)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, faceted, tolerance, curves);
        }

        /// <summary>
        /// Constructs new mesh from the current one, with displacement applied to it.
        /// </summary>
        /// <param name="displacement">Information on mesh displacement.</param>
        /// <returns>A new mesh with shutlining.</returns>
        /// <exception cref="ArgumentNullException">If displacer is null.</exception>
        /// <exception cref="InvalidOperationException">If displacement failed
        /// because of an error. The exception message specifies the error.</exception>
        public static Mesh WithDisplacement(this Mesh mesh, MeshDisplacementInfo displacement)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, displacement);
        }

        /// <summary>
        /// Constructs new mesh from the current one, with displacement applied to it.
        /// </summary>
        /// <param name="displacement">Information on mesh displacement.</param>
        /// <returns>A new mesh with shutlining.</returns>
        /// <exception cref="ArgumentNullException">If displacer is null.</exception>
        /// <exception cref="InvalidOperationException">If displacement failed
        /// because of an error. The exception message specifies the error.</exception>
        public static Mesh WithDisplacement(Remote<Mesh> mesh, MeshDisplacementInfo displacement)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, displacement);
        }

        /// <summary>
        /// Constructs new mesh from the current one, with edge softening applied to it.
        /// </summary>
        /// <param name="softeningRadius">The softening radius.</param>
        /// <param name="chamfer">Specifies whether to chamfer the edges.</param>
        /// <param name="faceted">Specifies whether the edges are faceted.</param>
        /// <param name="force">Specifies whether to soften edges despite too large a radius.</param>
        /// <param name="angleThreshold">Threshold angle (in degrees) which controls whether an edge is softened or not.
        /// The angle refers to the angles between the adjacent faces of an edge.</param>
        /// <returns>A new mesh with soft edges.</returns>
        /// <exception cref="InvalidOperationException">If displacement failed
        /// because of an error. The exception message specifies the error.</exception>
        public static Mesh WithEdgeSoftening(this Mesh mesh, double softeningRadius, bool chamfer, bool faceted, bool force, double angleThreshold)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, softeningRadius, chamfer, faceted, force, angleThreshold);
        }

        /// <summary>
        /// Constructs new mesh from the current one, with edge softening applied to it.
        /// </summary>
        /// <param name="softeningRadius">The softening radius.</param>
        /// <param name="chamfer">Specifies whether to chamfer the edges.</param>
        /// <param name="faceted">Specifies whether the edges are faceted.</param>
        /// <param name="force">Specifies whether to soften edges despite too large a radius.</param>
        /// <param name="angleThreshold">Threshold angle (in degrees) which controls whether an edge is softened or not.
        /// The angle refers to the angles between the adjacent faces of an edge.</param>
        /// <returns>A new mesh with soft edges.</returns>
        /// <exception cref="InvalidOperationException">If displacement failed
        /// because of an error. The exception message specifies the error.</exception>
        public static Mesh WithEdgeSoftening(Remote<Mesh> mesh, double softeningRadius, bool chamfer, bool faceted, bool force, double angleThreshold)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), mesh, softeningRadius, chamfer, faceted, force, angleThreshold);
        }

        /// <summary>
        /// Create QuadRemesh from a Brep
        /// Set Brep Face Mode by setting QuadRemeshParameters.PreserveMeshArrayEdgesMode
        /// </summary>
        /// <param name="brep"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static Mesh QuadRemeshBrep(Brep brep, QuadRemeshParameters parameters)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), brep, parameters);
        }

        /// <summary>
        /// Create QuadRemesh from a Brep
        /// Set Brep Face Mode by setting QuadRemeshParameters.PreserveMeshArrayEdgesMode
        /// </summary>
        /// <param name="brep"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static Mesh QuadRemeshBrep(Remote<Brep> brep, QuadRemeshParameters parameters)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), brep, parameters);
        }

        /// <summary>
        /// Create Quad Remesh from a Brep
        /// </summary>
        /// <param name="brep">
        /// Set Brep Face Mode by setting QuadRemeshParameters.PreserveMeshArrayEdgesMode
        /// </param>
        /// <param name="parameters"></param>
        /// <param name="guideCurves">
        /// A curve array used to influence mesh face layout
        /// The curves should touch the input mesh
        /// Set Guide Curve Influence by using QuadRemeshParameters.GuideCurveInfluence
        /// </param>
        /// <returns></returns>
        public static Mesh QuadRemeshBrep(Brep brep, QuadRemeshParameters parameters, IEnumerable<Curve> guideCurves)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), brep, parameters, guideCurves);
        }

        /// <summary>
        /// Create Quad Remesh from a Brep
        /// </summary>
        /// <param name="brep">
        /// Set Brep Face Mode by setting QuadRemeshParameters.PreserveMeshArrayEdgesMode
        /// </param>
        /// <param name="parameters"></param>
        /// <param name="guideCurves">
        /// A curve array used to influence mesh face layout
        /// The curves should touch the input mesh
        /// Set Guide Curve Influence by using QuadRemeshParameters.GuideCurveInfluence
        /// </param>
        /// <returns></returns>
        public static Mesh QuadRemeshBrep(Remote<Brep> brep, QuadRemeshParameters parameters, Remote<IEnumerable<Curve>> guideCurves)
        {
            return ComputeServer.Post<Mesh>(ApiAddress(), brep, parameters, guideCurves);
        }

        /// <summary>
        /// Quad remesh this Brep asynchronously.
        /// </summary>
        /// <param name="brep">
        /// Set Brep Face Mode by setting QuadRemeshParameters.PreserveMeshArrayEdgesMode
        /// </param>
        /// <param name="parameters"></param>
        /// <param name="progress"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public static Task<Mesh> QuadRemeshBrepAsync(Brep brep, QuadRemeshParameters parameters, IProgress<int> progress, CancellationToken cancelToken)
        {
            return ComputeServer.Post<Task<Mesh>>(ApiAddress(), brep, parameters, progress, cancelToken);
        }

        /// <summary>
        /// Quad remesh this Brep asynchronously.
        /// </summary>
        /// <param name="brep">
        /// Set Brep Face Mode by setting QuadRemeshParameters.PreserveMeshArrayEdgesMode
        /// </param>
        /// <param name="parameters"></param>
        /// <param name="progress"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public static Task<Mesh> QuadRemeshBrepAsync(Remote<Brep> brep, QuadRemeshParameters parameters, IProgress<int> progress, CancellationToken cancelToken)
        {
            return ComputeServer.Post<Task<Mesh>>(ApiAddress(), brep, parameters, progress, cancelToken);
        }

        /// <summary>
        /// Quad remesh this Brep asynchronously.
        /// </summary>
        /// <param name="brep">
        /// Set Brep Face Mode by setting QuadRemeshParameters.PreserveMeshArrayEdgesMode
        /// </param>
        /// <param name="parameters"></param>
        /// <param name="guideCurves">
        /// A curve array used to influence mesh face layout
        /// The curves should touch the input mesh
        /// Set Guide Curve Influence by using QuadRemeshParameters.GuideCurveInfluence
        /// </param>
        /// <param name="progress"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public static Task<Mesh> QuadRemeshBrepAsync(Brep brep, QuadRemeshParameters parameters, IEnumerable<Curve> guideCurves, IProgress<int> progress, CancellationToken cancelToken)
        {
            return ComputeServer.Post<Task<Mesh>>(ApiAddress(), brep, parameters, guideCurves, progress, cancelToken);
        }

        /// <summary>
        /// Quad remesh this Brep asynchronously.
        /// </summary>
        /// <param name="brep">
        /// Set Brep Face Mode by setting QuadRemeshParameters.PreserveMeshArrayEdgesMode
        /// </param>
        /// <param name="parameters"></param>
        /// <param name="guideCurves">
        /// A curve array used to influence mesh face layout
        /// The curves should touch the input mesh
        /// Set Guide Curve Influence by using QuadRemeshParameters.GuideCurveInfluence
        /// </param>
        /// <param name="progress"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public static Task<Mesh> QuadRemeshBrepAsync(Remote<Brep> brep, QuadRemeshParameters parameters, Remote<IEnumerable<Curve>> guideCurves, IProgress<int> progress, CancellationToken cancelToken)
        {
            return ComputeServer.Post<Task<Mesh>>(ApiAddress(), brep, parameters, guideCurves, progress, cancelToken);
        }

        /// <summary>
        /// Quad remesh this mesh.
        /// </summary>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static Mesh QuadRemesh(this Mesh mesh, out Mesh updatedInstance, QuadRemeshParameters parameters)
        {
            return ComputeServer.Post<Mesh, Mesh>(ApiAddress(), out updatedInstance, mesh, parameters);
        }

        /// <summary>
        /// Quad remesh this mesh.
        /// </summary>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static Mesh QuadRemesh(Remote<Mesh> mesh, out Mesh updatedInstance, QuadRemeshParameters parameters)
        {
            return ComputeServer.Post<Mesh, Mesh>(ApiAddress(), out updatedInstance, mesh, parameters);
        }

        /// <summary>
        /// Quad remesh this mesh.
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="guideCurves">
        /// A curve array used to influence mesh face layout
        /// The curves should touch the input mesh
        /// Set Guide Curve Influence by using QuadRemeshParameters.GuideCurveInfluence
        /// </param>
        /// <returns></returns>
        public static Mesh QuadRemesh(this Mesh mesh, out Mesh updatedInstance, QuadRemeshParameters parameters, IEnumerable<Curve> guideCurves)
        {
            return ComputeServer.Post<Mesh, Mesh>(ApiAddress(), out updatedInstance, mesh, parameters, guideCurves);
        }

        /// <summary>
        /// Quad remesh this mesh.
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="guideCurves">
        /// A curve array used to influence mesh face layout
        /// The curves should touch the input mesh
        /// Set Guide Curve Influence by using QuadRemeshParameters.GuideCurveInfluence
        /// </param>
        /// <returns></returns>
        public static Mesh QuadRemesh(Remote<Mesh> mesh, out Mesh updatedInstance, QuadRemeshParameters parameters, Remote<IEnumerable<Curve>> guideCurves)
        {
            return ComputeServer.Post<Mesh, Mesh>(ApiAddress(), out updatedInstance, mesh, parameters, guideCurves);
        }

        /// <summary>
        /// Quad remesh this mesh asynchronously.
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="progress"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public static Task<Mesh> QuadRemeshAsync(this Mesh mesh, out Mesh updatedInstance, QuadRemeshParameters parameters, IProgress<int> progress, CancellationToken cancelToken)
        {
            return ComputeServer.Post<Task<Mesh>, Mesh>(ApiAddress(), out updatedInstance, mesh, parameters, progress, cancelToken);
        }

        /// <summary>
        /// Quad remesh this mesh asynchronously.
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="progress"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public static Task<Mesh> QuadRemeshAsync(Remote<Mesh> mesh, out Mesh updatedInstance, QuadRemeshParameters parameters, IProgress<int> progress, CancellationToken cancelToken)
        {
            return ComputeServer.Post<Task<Mesh>, Mesh>(ApiAddress(), out updatedInstance, mesh, parameters, progress, cancelToken);
        }

        /// <summary>
        /// Quad remesh this mesh asynchronously.
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="guideCurves">
        /// A curve array used to influence mesh face layout
        /// The curves should touch the input mesh
        /// Set Guide Curve Influence by using QuadRemeshParameters.GuideCurveInfluence
        /// </param>
        /// <param name="progress"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public static Task<Mesh> QuadRemeshAsync(this Mesh mesh, out Mesh updatedInstance, QuadRemeshParameters parameters, IEnumerable<Curve> guideCurves, IProgress<int> progress, CancellationToken cancelToken)
        {
            return ComputeServer.Post<Task<Mesh>, Mesh>(ApiAddress(), out updatedInstance, mesh, parameters, guideCurves, progress, cancelToken);
        }

        /// <summary>
        /// Quad remesh this mesh asynchronously.
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="guideCurves">
        /// A curve array used to influence mesh face layout
        /// The curves should touch the input mesh
        /// Set Guide Curve Influence by using QuadRemeshParameters.GuideCurveInfluence
        /// </param>
        /// <param name="progress"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public static Task<Mesh> QuadRemeshAsync(Remote<Mesh> mesh, out Mesh updatedInstance, QuadRemeshParameters parameters, Remote<IEnumerable<Curve>> guideCurves, IProgress<int> progress, CancellationToken cancelToken)
        {
            return ComputeServer.Post<Task<Mesh>, Mesh>(ApiAddress(), out updatedInstance, mesh, parameters, guideCurves, progress, cancelToken);
        }

        /// <summary>
        /// Quad remesh this mesh asynchronously.
        /// </summary>
        /// <param name="faceBlocks"></param>
        /// <param name="parameters"></param>
        /// <param name="guideCurves">
        /// A curve array used to influence mesh face layout
        /// The curves should touch the input mesh
        /// Set Guide Curve Influence by using QuadRemeshParameters.GuideCurveInfluence
        /// </param>
        /// <param name="progress"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public static Task<Mesh> QuadRemeshAsync(this Mesh mesh, out Mesh updatedInstance, IEnumerable<int> faceBlocks, QuadRemeshParameters parameters, IEnumerable<Curve> guideCurves, IProgress<int> progress, CancellationToken cancelToken)
        {
            return ComputeServer.Post<Task<Mesh>, Mesh>(ApiAddress(), out updatedInstance, mesh, faceBlocks, parameters, guideCurves, progress, cancelToken);
        }

        /// <summary>
        /// Quad remesh this mesh asynchronously.
        /// </summary>
        /// <param name="faceBlocks"></param>
        /// <param name="parameters"></param>
        /// <param name="guideCurves">
        /// A curve array used to influence mesh face layout
        /// The curves should touch the input mesh
        /// Set Guide Curve Influence by using QuadRemeshParameters.GuideCurveInfluence
        /// </param>
        /// <param name="progress"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public static Task<Mesh> QuadRemeshAsync(Remote<Mesh> mesh, out Mesh updatedInstance, IEnumerable<int> faceBlocks, QuadRemeshParameters parameters, Remote<IEnumerable<Curve>> guideCurves, IProgress<int> progress, CancellationToken cancelToken)
        {
            return ComputeServer.Post<Task<Mesh>, Mesh>(ApiAddress(), out updatedInstance, mesh, faceBlocks, parameters, guideCurves, progress, cancelToken);
        }

        /// <summary>
        /// Reduce polygon count
        /// </summary>
        /// <param name="desiredPolygonCount">desired or target number of faces</param>
        /// <param name="allowDistortion">
        /// If true mesh appearance is not changed even if the target polygon count is not reached
        /// </param>
        /// <param name="accuracy">Integer from 1 to 10 telling how accurate reduction algorithm
        ///  to use. Greater number gives more accurate results
        /// </param>
        /// <param name="normalizeSize">If true mesh is fitted to an axis aligned unit cube until reduction is complete</param>
        /// <returns>True if mesh is successfully reduced and false if mesh could not be reduced for some reason.</returns>
        public static bool Reduce(this Mesh mesh, out Mesh updatedInstance, int desiredPolygonCount, bool allowDistortion, int accuracy, bool normalizeSize)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, desiredPolygonCount, allowDistortion, accuracy, normalizeSize);
        }

        /// <summary>
        /// Reduce polygon count
        /// </summary>
        /// <param name="desiredPolygonCount">desired or target number of faces</param>
        /// <param name="allowDistortion">
        /// If true mesh appearance is not changed even if the target polygon count is not reached
        /// </param>
        /// <param name="accuracy">Integer from 1 to 10 telling how accurate reduction algorithm
        ///  to use. Greater number gives more accurate results
        /// </param>
        /// <param name="normalizeSize">If true mesh is fitted to an axis aligned unit cube until reduction is complete</param>
        /// <returns>True if mesh is successfully reduced and false if mesh could not be reduced for some reason.</returns>
        public static bool Reduce(Remote<Mesh> mesh, out Mesh updatedInstance, int desiredPolygonCount, bool allowDistortion, int accuracy, bool normalizeSize)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, desiredPolygonCount, allowDistortion, accuracy, normalizeSize);
        }

        /// <summary>
        /// Reduce polygon count
        /// </summary>
        /// <param name="desiredPolygonCount">desired or target number of faces</param>
        /// <param name="allowDistortion">
        /// If true mesh appearance is not changed even if the target polygon count is not reached
        /// </param>
        /// <param name="accuracy">Integer from 1 to 10 telling how accurate reduction algorithm
        ///  to use. Greater number gives more accurate results
        /// </param>
        /// <param name="normalizeSize">If true mesh is fitted to an axis aligned unit cube until reduction is complete</param>
        /// <param name="threaded">
        /// If True then will run computation inside a worker thread and ignore any provided CancellationTokens and ProgressReporters.
        /// If False then will run on main thread.</param>
        /// <returns>True if mesh is successfully reduced and false if mesh could not be reduced for some reason.</returns>
        public static bool Reduce(this Mesh mesh, out Mesh updatedInstance, int desiredPolygonCount, bool allowDistortion, int accuracy, bool normalizeSize, bool threaded)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, desiredPolygonCount, allowDistortion, accuracy, normalizeSize, threaded);
        }

        /// <summary>
        /// Reduce polygon count
        /// </summary>
        /// <param name="desiredPolygonCount">desired or target number of faces</param>
        /// <param name="allowDistortion">
        /// If true mesh appearance is not changed even if the target polygon count is not reached
        /// </param>
        /// <param name="accuracy">Integer from 1 to 10 telling how accurate reduction algorithm
        ///  to use. Greater number gives more accurate results
        /// </param>
        /// <param name="normalizeSize">If true mesh is fitted to an axis aligned unit cube until reduction is complete</param>
        /// <param name="threaded">
        /// If True then will run computation inside a worker thread and ignore any provided CancellationTokens and ProgressReporters.
        /// If False then will run on main thread.</param>
        /// <returns>True if mesh is successfully reduced and false if mesh could not be reduced for some reason.</returns>
        public static bool Reduce(Remote<Mesh> mesh, out Mesh updatedInstance, int desiredPolygonCount, bool allowDistortion, int accuracy, bool normalizeSize, bool threaded)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, desiredPolygonCount, allowDistortion, accuracy, normalizeSize, threaded);
        }

        /// <summary>Reduce polygon count</summary>
        /// <param name="desiredPolygonCount">desired or target number of faces</param>
        /// <param name="allowDistortion">
        /// If true mesh appearance is not changed even if the target polygon count is not reached
        /// </param>
        /// <param name="accuracy">Integer from 1 to 10 telling how accurate reduction algorithm
        ///  to use. Greater number gives more accurate results
        /// </param>
        /// <param name="normalizeSize">If true mesh is fitted to an axis aligned unit cube until reduction is complete</param>
        /// <param name="cancelToken"></param>
        /// <param name="progress"></param>
        /// <param name="problemDescription"></param>
        /// <returns>True if mesh is successfully reduced and false if mesh could not be reduced for some reason.</returns>
        public static bool Reduce(this Mesh mesh, out Mesh updatedInstance, int desiredPolygonCount, bool allowDistortion, int accuracy, bool normalizeSize, CancellationToken cancelToken, IProgress<double> progress, out string problemDescription)
        {
            return ComputeServer.Post<bool, string>(ApiAddress(), out problemDescription, mesh, desiredPolygonCount, allowDistortion, accuracy, normalizeSize, cancelToken, progress);
        }

        /// <summary>Reduce polygon count</summary>
        /// <param name="desiredPolygonCount">desired or target number of faces</param>
        /// <param name="allowDistortion">
        /// If true mesh appearance is not changed even if the target polygon count is not reached
        /// </param>
        /// <param name="accuracy">Integer from 1 to 10 telling how accurate reduction algorithm
        ///  to use. Greater number gives more accurate results
        /// </param>
        /// <param name="normalizeSize">If true mesh is fitted to an axis aligned unit cube until reduction is complete</param>
        /// <param name="cancelToken"></param>
        /// <param name="progress"></param>
        /// <param name="problemDescription"></param>
        /// <returns>True if mesh is successfully reduced and false if mesh could not be reduced for some reason.</returns>
        public static bool Reduce(Remote<Mesh> mesh, out Mesh updatedInstance, int desiredPolygonCount, bool allowDistortion, int accuracy, bool normalizeSize, CancellationToken cancelToken, IProgress<double> progress, out string problemDescription)
        {
            return ComputeServer.Post<bool, string>(ApiAddress(), out problemDescription, mesh, desiredPolygonCount, allowDistortion, accuracy, normalizeSize, cancelToken, progress);
        }

        /// <summary>Reduce polygon count</summary>
        /// <param name="desiredPolygonCount">desired or target number of faces</param>
        /// <param name="allowDistortion">
        /// If true mesh appearance is not changed even if the target polygon count is not reached
        /// </param>
        /// <param name="accuracy">Integer from 1 to 10 telling how accurate reduction algorithm
        ///  to use. Greater number gives more accurate results
        /// </param>
        /// <param name="normalizeSize">If true mesh is fitted to an axis aligned unit cube until reduction is complete</param>
        /// <param name="cancelToken"></param>
        /// <param name="progress"></param>
        /// <param name="problemDescription"></param>
        /// <param name="threaded">
        /// If True then will run computation inside a worker thread and ignore any provided CancellationTokens and ProgressReporters.
        /// If False then will run on main thread.</param>
        /// <returns>True if mesh is successfully reduced and false if mesh could not be reduced for some reason.</returns>
        public static bool Reduce(this Mesh mesh, out Mesh updatedInstance, int desiredPolygonCount, bool allowDistortion, int accuracy, bool normalizeSize, CancellationToken cancelToken, IProgress<double> progress, out string problemDescription, bool threaded)
        {
            return ComputeServer.Post<bool, string>(ApiAddress(), out problemDescription, mesh, desiredPolygonCount, allowDistortion, accuracy, normalizeSize, cancelToken, progress, threaded);
        }

        /// <summary>Reduce polygon count</summary>
        /// <param name="desiredPolygonCount">desired or target number of faces</param>
        /// <param name="allowDistortion">
        /// If true mesh appearance is not changed even if the target polygon count is not reached
        /// </param>
        /// <param name="accuracy">Integer from 1 to 10 telling how accurate reduction algorithm
        ///  to use. Greater number gives more accurate results
        /// </param>
        /// <param name="normalizeSize">If true mesh is fitted to an axis aligned unit cube until reduction is complete</param>
        /// <param name="cancelToken"></param>
        /// <param name="progress"></param>
        /// <param name="problemDescription"></param>
        /// <param name="threaded">
        /// If True then will run computation inside a worker thread and ignore any provided CancellationTokens and ProgressReporters.
        /// If False then will run on main thread.</param>
        /// <returns>True if mesh is successfully reduced and false if mesh could not be reduced for some reason.</returns>
        public static bool Reduce(Remote<Mesh> mesh, out Mesh updatedInstance, int desiredPolygonCount, bool allowDistortion, int accuracy, bool normalizeSize, CancellationToken cancelToken, IProgress<double> progress, out string problemDescription, bool threaded)
        {
            return ComputeServer.Post<bool, string>(ApiAddress(), out problemDescription, mesh, desiredPolygonCount, allowDistortion, accuracy, normalizeSize, cancelToken, progress, threaded);
        }

        /// <summary>Reduce polygon count</summary>
        /// <param name="parameters">Parameters</param>
        /// <returns>True if mesh is successfully reduced and false if mesh could not be reduced for some reason.</returns>
        public static bool Reduce(this Mesh mesh, out Mesh updatedInstance, ReduceMeshParameters parameters)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, parameters);
        }

        /// <summary>Reduce polygon count</summary>
        /// <param name="parameters">Parameters</param>
        /// <returns>True if mesh is successfully reduced and false if mesh could not be reduced for some reason.</returns>
        public static bool Reduce(Remote<Mesh> mesh, out Mesh updatedInstance, ReduceMeshParameters parameters)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, parameters);
        }

        /// <summary>Reduce polygon count</summary>
        /// <param name="parameters">Parameters</param>
        /// <param name="threaded">
        /// If True then will run computation inside a worker thread and ignore any provided CancellationTokens and ProgressReporters.
        /// If False then will run on main thread.</param>
        /// <returns>True if mesh is successfully reduced and false if mesh could not be reduced for some reason.</returns>
        public static bool Reduce(this Mesh mesh, out Mesh updatedInstance, ReduceMeshParameters parameters, bool threaded)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, parameters, threaded);
        }

        /// <summary>Reduce polygon count</summary>
        /// <param name="parameters">Parameters</param>
        /// <param name="threaded">
        /// If True then will run computation inside a worker thread and ignore any provided CancellationTokens and ProgressReporters.
        /// If False then will run on main thread.</param>
        /// <returns>True if mesh is successfully reduced and false if mesh could not be reduced for some reason.</returns>
        public static bool Reduce(Remote<Mesh> mesh, out Mesh updatedInstance, ReduceMeshParameters parameters, bool threaded)
        {
            return ComputeServer.Post<bool, Mesh>(ApiAddress(), out updatedInstance, mesh, parameters, threaded);
        }

        /// <summary>
        /// Compute thickness metrics for this mesh.
        /// </summary>
        /// <param name="meshes">Meshes to include in thickness analysis.</param>
        /// <param name="maximumThickness">Maximum thickness to consider. Use as small a thickness as possible to speed up the solver.</param>
        /// <returns>Array of thickness measurements.</returns>
        public static MeshThicknessMeasurement[] ComputeThickness(IEnumerable<Mesh> meshes, double maximumThickness)
        {
            return ComputeServer.Post<MeshThicknessMeasurement[]>(ApiAddress(), meshes, maximumThickness);
        }

        /// <summary>
        /// Compute thickness metrics for this mesh.
        /// </summary>
        /// <param name="meshes">Meshes to include in thickness analysis.</param>
        /// <param name="maximumThickness">Maximum thickness to consider. Use as small a thickness as possible to speed up the solver.</param>
        /// <returns>Array of thickness measurements.</returns>
        public static MeshThicknessMeasurement[] ComputeThickness(Remote<IEnumerable<Mesh>> meshes, double maximumThickness)
        {
            return ComputeServer.Post<MeshThicknessMeasurement[]>(ApiAddress(), meshes, maximumThickness);
        }

        /// <summary>
        /// Compute thickness metrics for this mesh.
        /// </summary>
        /// <param name="meshes">Meshes to include in thickness analysis.</param>
        /// <param name="maximumThickness">Maximum thickness to consider. Use as small a thickness as possible to speed up the solver.</param>
        /// <param name="cancelToken">Computation cancellation token.</param>
        /// <returns>Array of thickness measurements.</returns>
        public static MeshThicknessMeasurement[] ComputeThickness(IEnumerable<Mesh> meshes, double maximumThickness, System.Threading.CancellationToken cancelToken)
        {
            return ComputeServer.Post<MeshThicknessMeasurement[]>(ApiAddress(), meshes, maximumThickness, cancelToken);
        }

        /// <summary>
        /// Compute thickness metrics for this mesh.
        /// </summary>
        /// <param name="meshes">Meshes to include in thickness analysis.</param>
        /// <param name="maximumThickness">Maximum thickness to consider. Use as small a thickness as possible to speed up the solver.</param>
        /// <param name="cancelToken">Computation cancellation token.</param>
        /// <returns>Array of thickness measurements.</returns>
        public static MeshThicknessMeasurement[] ComputeThickness(Remote<IEnumerable<Mesh>> meshes, double maximumThickness, System.Threading.CancellationToken cancelToken)
        {
            return ComputeServer.Post<MeshThicknessMeasurement[]>(ApiAddress(), meshes, maximumThickness, cancelToken);
        }

        /// <summary>
        /// Compute thickness metrics for this mesh.
        /// </summary>
        /// <param name="meshes">Meshes to include in thickness analysis.</param>
        /// <param name="maximumThickness">Maximum thickness to consider. Use as small a thickness as possible to speed up the solver.</param>
        /// <param name="sharpAngle">Sharpness angle in radians.</param>
        /// <param name="cancelToken">Computation cancellation token.</param>
        /// <returns>Array of thickness measurements.</returns>
        public static MeshThicknessMeasurement[] ComputeThickness(IEnumerable<Mesh> meshes, double maximumThickness, double sharpAngle, System.Threading.CancellationToken cancelToken)
        {
            return ComputeServer.Post<MeshThicknessMeasurement[]>(ApiAddress(), meshes, maximumThickness, sharpAngle, cancelToken);
        }

        /// <summary>
        /// Compute thickness metrics for this mesh.
        /// </summary>
        /// <param name="meshes">Meshes to include in thickness analysis.</param>
        /// <param name="maximumThickness">Maximum thickness to consider. Use as small a thickness as possible to speed up the solver.</param>
        /// <param name="sharpAngle">Sharpness angle in radians.</param>
        /// <param name="cancelToken">Computation cancellation token.</param>
        /// <returns>Array of thickness measurements.</returns>
        public static MeshThicknessMeasurement[] ComputeThickness(Remote<IEnumerable<Mesh>> meshes, double maximumThickness, double sharpAngle, System.Threading.CancellationToken cancelToken)
        {
            return ComputeServer.Post<MeshThicknessMeasurement[]>(ApiAddress(), meshes, maximumThickness, sharpAngle, cancelToken);
        }

        /// <summary>
        /// Constructs contour curves for a mesh, sectioned along a linear axis.
        /// </summary>
        /// <param name="meshToContour">A mesh to contour.</param>
        /// <param name="contourStart">A start point of the contouring axis.</param>
        /// <param name="contourEnd">An end point of the contouring axis.</param>
        /// <param name="interval">An interval distance.</param>
        /// <returns>An array of curves. This array can be empty.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_makerhinocontours.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_makerhinocontours.cs' lang='cs'/>
        /// <code source='examples\py\ex_makerhinocontours.py' lang='py'/>
        /// </example>
        public static Curve[] CreateContourCurves(Mesh meshToContour, Point3d contourStart, Point3d contourEnd, double interval)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), meshToContour, contourStart, contourEnd, interval);
        }

        /// <summary>
        /// Constructs contour curves for a mesh, sectioned along a linear axis.
        /// </summary>
        /// <param name="meshToContour">A mesh to contour.</param>
        /// <param name="contourStart">A start point of the contouring axis.</param>
        /// <param name="contourEnd">An end point of the contouring axis.</param>
        /// <param name="interval">An interval distance.</param>
        /// <returns>An array of curves. This array can be empty.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_makerhinocontours.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_makerhinocontours.cs' lang='cs'/>
        /// <code source='examples\py\ex_makerhinocontours.py' lang='py'/>
        /// </example>
        public static Curve[] CreateContourCurves(Remote<Mesh> meshToContour, Point3d contourStart, Point3d contourEnd, double interval)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), meshToContour, contourStart, contourEnd, interval);
        }

        /// <summary>
        /// Constructs contour curves for a mesh, sectioned at a plane.
        /// </summary>
        /// <param name="meshToContour">A mesh to contour.</param>
        /// <param name="sectionPlane">A cutting plane.</param>
        /// <returns>An array of curves. This array can be empty.</returns>
        public static Curve[] CreateContourCurves(Mesh meshToContour, Plane sectionPlane)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), meshToContour, sectionPlane);
        }

        /// <summary>
        /// Constructs contour curves for a mesh, sectioned at a plane.
        /// </summary>
        /// <param name="meshToContour">A mesh to contour.</param>
        /// <param name="sectionPlane">A cutting plane.</param>
        /// <returns>An array of curves. This array can be empty.</returns>
        public static Curve[] CreateContourCurves(Remote<Mesh> meshToContour, Plane sectionPlane)
        {
            return ComputeServer.Post<Curve[]>(ApiAddress(), meshToContour, sectionPlane);
        }
    }

    public static class AreaMassPropertiesCompute
    {
        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return ComputeServer.ApiAddress(typeof(AreaMassProperties), caller);
        }

        /// <summary>
        /// Actively reclaims unmanaged resources that this instance uses.
        /// </summary>
        public static AreaMassProperties Dispose(this AreaMassProperties areamassproperties)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), areamassproperties);
        }

        /// <summary>
        /// Computes an AreaMassProperties for a closed planar curve.
        /// </summary>
        /// <param name="closedPlanarCurve">Curve to measure.</param>
        /// <returns>The AreaMassProperties for the given curve or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When closedPlanarCurve is null.</exception>
        public static AreaMassProperties Compute(Curve closedPlanarCurve)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), closedPlanarCurve);
        }

        /// <summary>
        /// Computes an AreaMassProperties for a closed planar curve.
        /// </summary>
        /// <param name="closedPlanarCurve">Curve to measure.</param>
        /// <returns>The AreaMassProperties for the given curve or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When closedPlanarCurve is null.</exception>
        public static AreaMassProperties Compute(Remote<Curve> closedPlanarCurve)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), closedPlanarCurve);
        }

        /// <summary>
        /// Computes an AreaMassProperties for a closed planar curve.
        /// </summary>
        /// <param name="closedPlanarCurve">Curve to measure.</param>
        /// <param name="planarTolerance">absolute tolerance used to insure the closed curve is planar</param>
        /// <returns>The AreaMassProperties for the given curve or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When closedPlanarCurve is null.</exception>
        public static AreaMassProperties Compute(Curve closedPlanarCurve, double planarTolerance)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), closedPlanarCurve, planarTolerance);
        }

        /// <summary>
        /// Computes an AreaMassProperties for a closed planar curve.
        /// </summary>
        /// <param name="closedPlanarCurve">Curve to measure.</param>
        /// <param name="planarTolerance">absolute tolerance used to insure the closed curve is planar</param>
        /// <returns>The AreaMassProperties for the given curve or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When closedPlanarCurve is null.</exception>
        public static AreaMassProperties Compute(Remote<Curve> closedPlanarCurve, double planarTolerance)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), closedPlanarCurve, planarTolerance);
        }

        /// <summary>
        /// Computes an AreaMassProperties for a hatch.
        /// </summary>
        /// <param name="hatch">Hatch to measure.</param>
        /// <returns>The AreaMassProperties for the given hatch or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When hatch is null.</exception>
        public static AreaMassProperties Compute(Hatch hatch)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), hatch);
        }

        /// <summary>
        /// Computes an AreaMassProperties for a mesh.
        /// </summary>
        /// <param name="mesh">Mesh to measure.</param>
        /// <returns>The AreaMassProperties for the given Mesh or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When mesh is null.</exception>
        public static AreaMassProperties Compute(Mesh mesh)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Computes an AreaMassProperties for a mesh.
        /// </summary>
        /// <param name="mesh">Mesh to measure.</param>
        /// <returns>The AreaMassProperties for the given Mesh or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When mesh is null.</exception>
        public static AreaMassProperties Compute(Remote<Mesh> mesh)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Compute the AreaMassProperties for a single Mesh.
        /// </summary>
        /// <param name="mesh">Mesh to measure.</param>
        /// <param name="area">true to calculate area.</param>
        /// <param name="firstMoments">true to calculate area first moments, area, and area centroid.</param>
        /// <param name="secondMoments">true to calculate area second moments.</param>
        /// <param name="productMoments">true to calculate area product moments.</param>
        /// <returns>The AreaMassProperties for the given Mesh or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When mesh is null.</exception>
        public static AreaMassProperties Compute(Mesh mesh, bool area, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), mesh, area, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Compute the AreaMassProperties for a single Mesh.
        /// </summary>
        /// <param name="mesh">Mesh to measure.</param>
        /// <param name="area">true to calculate area.</param>
        /// <param name="firstMoments">true to calculate area first moments, area, and area centroid.</param>
        /// <param name="secondMoments">true to calculate area second moments.</param>
        /// <param name="productMoments">true to calculate area product moments.</param>
        /// <returns>The AreaMassProperties for the given Mesh or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When mesh is null.</exception>
        public static AreaMassProperties Compute(Remote<Mesh> mesh, bool area, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), mesh, area, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Computes an AreaMassProperties for a brep.
        /// </summary>
        /// <param name="brep">Brep to measure.</param>
        /// <returns>The AreaMassProperties for the given Brep or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When brep is null.</exception>
        public static AreaMassProperties Compute(Brep brep)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), brep);
        }

        /// <summary>
        /// Computes an AreaMassProperties for a brep.
        /// </summary>
        /// <param name="brep">Brep to measure.</param>
        /// <returns>The AreaMassProperties for the given Brep or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When brep is null.</exception>
        public static AreaMassProperties Compute(Remote<Brep> brep)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), brep);
        }

        /// <summary>
        /// Compute the AreaMassProperties for a single Brep.
        /// </summary>
        /// <param name="brep">Brep to measure.</param>
        /// <param name="area">true to calculate area.</param>
        /// <param name="firstMoments">true to calculate area first moments, area, and area centroid.</param>
        /// <param name="secondMoments">true to calculate area second moments.</param>
        /// <param name="productMoments">true to calculate area product moments.</param>
        /// <returns>The AreaMassProperties for the given Brep or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When brep is null.</exception>
        public static AreaMassProperties Compute(Brep brep, bool area, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), brep, area, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Compute the AreaMassProperties for a single Brep.
        /// </summary>
        /// <param name="brep">Brep to measure.</param>
        /// <param name="area">true to calculate area.</param>
        /// <param name="firstMoments">true to calculate area first moments, area, and area centroid.</param>
        /// <param name="secondMoments">true to calculate area second moments.</param>
        /// <param name="productMoments">true to calculate area product moments.</param>
        /// <returns>The AreaMassProperties for the given Brep or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When brep is null.</exception>
        public static AreaMassProperties Compute(Remote<Brep> brep, bool area, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), brep, area, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Computes an AreaMassProperties for a surface.
        /// </summary>
        /// <param name="surface">Surface to measure.</param>
        /// <returns>The AreaMassProperties for the given Surface or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When surface is null.</exception>
        public static AreaMassProperties Compute(Surface surface)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), surface);
        }

        /// <summary>
        /// Computes an AreaMassProperties for a surface.
        /// </summary>
        /// <param name="surface">Surface to measure.</param>
        /// <returns>The AreaMassProperties for the given Surface or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When surface is null.</exception>
        public static AreaMassProperties Compute(Remote<Surface> surface)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), surface);
        }

        /// <summary>
        /// Compute the AreaMassProperties for a single Surface.
        /// </summary>
        /// <param name="surface">Surface to measure.</param>
        /// <param name="area">true to calculate area.</param>
        /// <param name="firstMoments">true to calculate area first moments, area, and area centroid.</param>
        /// <param name="secondMoments">true to calculate area second moments.</param>
        /// <param name="productMoments">true to calculate area product moments.</param>
        /// <returns>The AreaMassProperties for the given Surface or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When surface is null.</exception>
        public static AreaMassProperties Compute(Surface surface, bool area, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), surface, area, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Compute the AreaMassProperties for a single Surface.
        /// </summary>
        /// <param name="surface">Surface to measure.</param>
        /// <param name="area">true to calculate area.</param>
        /// <param name="firstMoments">true to calculate area first moments, area, and area centroid.</param>
        /// <param name="secondMoments">true to calculate area second moments.</param>
        /// <param name="productMoments">true to calculate area product moments.</param>
        /// <returns>The AreaMassProperties for the given Surface or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When surface is null.</exception>
        public static AreaMassProperties Compute(Remote<Surface> surface, bool area, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), surface, area, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Computes the Area properties for a collection of geometric objects. 
        /// At present only Breps, Surfaces, Meshes and Planar Closed Curves are supported.
        /// </summary>
        /// <param name="geometry">Objects to include in the area computation.</param>
        /// <returns>The Area properties for the entire collection or null on failure.</returns>
        public static AreaMassProperties Compute(IEnumerable<GeometryBase> geometry)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), geometry);
        }

        /// <summary>
        /// Computes the Area properties for a collection of geometric objects. 
        /// At present only Breps, Surfaces, Meshes and Planar Closed Curves are supported.
        /// </summary>
        /// <param name="geometry">Objects to include in the area computation.</param>
        /// <returns>The Area properties for the entire collection or null on failure.</returns>
        public static AreaMassProperties Compute(Remote<IEnumerable<GeometryBase>> geometry)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), geometry);
        }

        /// <summary>
        /// Computes the AreaMassProperties for a collection of geometric objects. 
        /// At present only Breps, Surfaces, Meshes and Planar Closed Curves are supported.
        /// </summary>
        /// <param name="geometry">Objects to include in the area computation.</param>
        /// <param name="area">true to calculate area.</param>
        /// <param name="firstMoments">true to calculate area first moments, area, and area centroid.</param>
        /// <param name="secondMoments">true to calculate area second moments.</param>
        /// <param name="productMoments">true to calculate area product moments.</param>
        /// <returns>The AreaMassProperties for the entire collection or null on failure.</returns>
        public static AreaMassProperties Compute(IEnumerable<GeometryBase> geometry, bool area, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), geometry, area, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Computes the AreaMassProperties for a collection of geometric objects. 
        /// At present only Breps, Surfaces, Meshes and Planar Closed Curves are supported.
        /// </summary>
        /// <param name="geometry">Objects to include in the area computation.</param>
        /// <param name="area">true to calculate area.</param>
        /// <param name="firstMoments">true to calculate area first moments, area, and area centroid.</param>
        /// <param name="secondMoments">true to calculate area second moments.</param>
        /// <param name="productMoments">true to calculate area product moments.</param>
        /// <returns>The AreaMassProperties for the entire collection or null on failure.</returns>
        public static AreaMassProperties Compute(Remote<IEnumerable<GeometryBase>> geometry, bool area, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<AreaMassProperties>(ApiAddress(), geometry, area, firstMoments, secondMoments, productMoments);
        }
    }

    public static class VolumeMassPropertiesCompute
    {
        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return ComputeServer.ApiAddress(typeof(VolumeMassProperties), caller);
        }

        /// <summary>
        /// Actively reclaims unmanaged resources that this instance uses.
        /// </summary>
        public static VolumeMassProperties Dispose(this VolumeMassProperties volumemassproperties)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), volumemassproperties);
        }

        /// <summary>
        /// Compute the VolumeMassProperties for a single Mesh.
        /// </summary>
        /// <param name="mesh">Mesh to measure.</param>
        /// <returns>The VolumeMassProperties for the given Mesh or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When mesh is null.</exception>
        public static VolumeMassProperties Compute(Mesh mesh)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Compute the VolumeMassProperties for a single Mesh.
        /// </summary>
        /// <param name="mesh">Mesh to measure.</param>
        /// <returns>The VolumeMassProperties for the given Mesh or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When mesh is null.</exception>
        public static VolumeMassProperties Compute(Remote<Mesh> mesh)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), mesh);
        }

        /// <summary>
        /// Compute the VolumeMassProperties for a single Mesh.
        /// </summary>
        /// <param name="mesh">Mesh to measure.</param>
        /// <param name="volume">true to calculate volume.</param>
        /// <param name="firstMoments">true to calculate volume first moments, volume, and volume centroid.</param>
        /// <param name="secondMoments">true to calculate volume second moments.</param>
        /// <param name="productMoments">true to calculate volume product moments.</param>
        /// <returns>The VolumeMassProperties for the given Mesh or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When Mesh is null.</exception>
        public static VolumeMassProperties Compute(Mesh mesh, bool volume, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), mesh, volume, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Compute the VolumeMassProperties for a single Mesh.
        /// </summary>
        /// <param name="mesh">Mesh to measure.</param>
        /// <param name="volume">true to calculate volume.</param>
        /// <param name="firstMoments">true to calculate volume first moments, volume, and volume centroid.</param>
        /// <param name="secondMoments">true to calculate volume second moments.</param>
        /// <param name="productMoments">true to calculate volume product moments.</param>
        /// <returns>The VolumeMassProperties for the given Mesh or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When Mesh is null.</exception>
        public static VolumeMassProperties Compute(Remote<Mesh> mesh, bool volume, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), mesh, volume, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Compute the VolumeMassProperties for a single Brep.
        /// </summary>
        /// <param name="brep">Brep to measure.</param>
        /// <returns>The VolumeMassProperties for the given Brep or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When brep is null.</exception>
        public static VolumeMassProperties Compute(Brep brep)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), brep);
        }

        /// <summary>
        /// Compute the VolumeMassProperties for a single Brep.
        /// </summary>
        /// <param name="brep">Brep to measure.</param>
        /// <returns>The VolumeMassProperties for the given Brep or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When brep is null.</exception>
        public static VolumeMassProperties Compute(Remote<Brep> brep)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), brep);
        }

        /// <summary>
        /// Compute the VolumeMassProperties for a single Brep.
        /// </summary>
        /// <param name="brep">Brep to measure.</param>
        /// <param name="volume">true to calculate volume.</param>
        /// <param name="firstMoments">true to calculate volume first moments, volume, and volume centroid.</param>
        /// <param name="secondMoments">true to calculate volume second moments.</param>
        /// <param name="productMoments">true to calculate volume product moments.</param>
        /// <returns>The VolumeMassProperties for the given Brep or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When brep is null.</exception>
        public static VolumeMassProperties Compute(Brep brep, bool volume, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), brep, volume, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Compute the VolumeMassProperties for a single Brep.
        /// </summary>
        /// <param name="brep">Brep to measure.</param>
        /// <param name="volume">true to calculate volume.</param>
        /// <param name="firstMoments">true to calculate volume first moments, volume, and volume centroid.</param>
        /// <param name="secondMoments">true to calculate volume second moments.</param>
        /// <param name="productMoments">true to calculate volume product moments.</param>
        /// <returns>The VolumeMassProperties for the given Brep or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When brep is null.</exception>
        public static VolumeMassProperties Compute(Remote<Brep> brep, bool volume, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), brep, volume, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Compute the VolumeMassProperties for a single Surface.
        /// </summary>
        /// <param name="surface">Surface to measure.</param>
        /// <returns>The VolumeMassProperties for the given Surface or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When surface is null.</exception>
        public static VolumeMassProperties Compute(Surface surface)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), surface);
        }

        /// <summary>
        /// Compute the VolumeMassProperties for a single Surface.
        /// </summary>
        /// <param name="surface">Surface to measure.</param>
        /// <returns>The VolumeMassProperties for the given Surface or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When surface is null.</exception>
        public static VolumeMassProperties Compute(Remote<Surface> surface)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), surface);
        }

        /// <summary>
        /// Compute the VolumeMassProperties for a single Surface.
        /// </summary>
        /// <param name="surface">Surface to measure.</param>
        /// <param name="volume">true to calculate volume.</param>
        /// <param name="firstMoments">true to calculate volume first moments, volume, and volume centroid.</param>
        /// <param name="secondMoments">true to calculate volume second moments.</param>
        /// <param name="productMoments">true to calculate volume product moments.</param>
        /// <returns>The VolumeMassProperties for the given Surface or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When surface is null.</exception>
        public static VolumeMassProperties Compute(Surface surface, bool volume, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), surface, volume, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Compute the VolumeMassProperties for a single Surface.
        /// </summary>
        /// <param name="surface">Surface to measure.</param>
        /// <param name="volume">true to calculate volume.</param>
        /// <param name="firstMoments">true to calculate volume first moments, volume, and volume centroid.</param>
        /// <param name="secondMoments">true to calculate volume second moments.</param>
        /// <param name="productMoments">true to calculate volume product moments.</param>
        /// <returns>The VolumeMassProperties for the given Surface or null on failure.</returns>
        /// <exception cref="System.ArgumentNullException">When surface is null.</exception>
        public static VolumeMassProperties Compute(Remote<Surface> surface, bool volume, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), surface, volume, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Computes the VolumeMassProperties for a collection of geometric objects. 
        /// At present only Breps, Surfaces, and Meshes are supported.
        /// </summary>
        /// <param name="geometry">Objects to include in the area computation.</param>
        /// <returns>The VolumeMassProperties for the entire collection or null on failure.</returns>
        public static VolumeMassProperties Compute(IEnumerable<GeometryBase> geometry)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), geometry);
        }

        /// <summary>
        /// Computes the VolumeMassProperties for a collection of geometric objects. 
        /// At present only Breps, Surfaces, and Meshes are supported.
        /// </summary>
        /// <param name="geometry">Objects to include in the area computation.</param>
        /// <returns>The VolumeMassProperties for the entire collection or null on failure.</returns>
        public static VolumeMassProperties Compute(Remote<IEnumerable<GeometryBase>> geometry)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), geometry);
        }

        /// <summary>
        /// Computes the VolumeMassProperties for a collection of geometric objects. 
        /// At present only Breps, Surfaces, Meshes and Planar Closed Curves are supported.
        /// </summary>
        /// <param name="geometry">Objects to include in the area computation.</param>
        /// <param name="volume">true to calculate volume.</param>
        /// <param name="firstMoments">true to calculate volume first moments, volume, and volume centroid.</param>
        /// <param name="secondMoments">true to calculate volume second moments.</param>
        /// <param name="productMoments">true to calculate volume product moments.</param>
        /// <returns>The VolumeMassProperties for the entire collection or null on failure.</returns>
        public static VolumeMassProperties Compute(IEnumerable<GeometryBase> geometry, bool volume, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), geometry, volume, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Computes the VolumeMassProperties for a collection of geometric objects. 
        /// At present only Breps, Surfaces, Meshes and Planar Closed Curves are supported.
        /// </summary>
        /// <param name="geometry">Objects to include in the area computation.</param>
        /// <param name="volume">true to calculate volume.</param>
        /// <param name="firstMoments">true to calculate volume first moments, volume, and volume centroid.</param>
        /// <param name="secondMoments">true to calculate volume second moments.</param>
        /// <param name="productMoments">true to calculate volume product moments.</param>
        /// <returns>The VolumeMassProperties for the entire collection or null on failure.</returns>
        public static VolumeMassProperties Compute(Remote<IEnumerable<GeometryBase>> geometry, bool volume, bool firstMoments, bool secondMoments, bool productMoments)
        {
            return ComputeServer.Post<VolumeMassProperties>(ApiAddress(), geometry, volume, firstMoments, secondMoments, productMoments);
        }

        /// <summary>
        /// Sum mass properties together to get an aggregate mass.
        /// </summary>
        /// <param name="summand">mass properties to add.</param>
        /// <returns>true if successful.</returns>
        public static bool Sum(this VolumeMassProperties volumemassproperties, out VolumeMassProperties updatedInstance, VolumeMassProperties summand)
        {
            return ComputeServer.Post<bool, VolumeMassProperties>(ApiAddress(), out updatedInstance, volumemassproperties, summand);
        }
    }

    public static class NurbsSurfaceCompute
    {
        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return ComputeServer.ApiAddress(typeof(NurbsSurface), caller);
        }

        /// <summary>
        /// Create a bi-cubic SubD friendly surface from a surface.
        /// </summary>
        /// <param name="surface">>Surface to rebuild as a SubD friendly surface.</param>
        /// <returns>A SubD friendly NURBS surface is successful, null otherwise.</returns>
        public static NurbsSurface CreateSubDFriendly(Surface surface)
        {
            return ComputeServer.Post<NurbsSurface>(ApiAddress(), surface);
        }

        /// <summary>
        /// Create a bi-cubic SubD friendly surface from a surface.
        /// </summary>
        /// <param name="surface">>Surface to rebuild as a SubD friendly surface.</param>
        /// <returns>A SubD friendly NURBS surface is successful, null otherwise.</returns>
        public static NurbsSurface CreateSubDFriendly(Remote<Surface> surface)
        {
            return ComputeServer.Post<NurbsSurface>(ApiAddress(), surface);
        }

        /// <summary>
        /// Creates a NURBS surface from a plane and additonal parameters.
        /// </summary>
        /// <param name="plane">The plane.</param>
        /// <param name="uInterval">The interval describing the extends of the output surface in the U direction.</param>
        /// <param name="vInterval">The interval describing the extends of the output surface in the V direction.</param>
        /// <param name="uDegree">The degree of the output surface in the U direction.</param>
        /// <param name="vDegree">The degree of the output surface in the V direction.</param>
        /// <param name="uPointCount">The number of control points of the output surface in the U direction.</param>
        /// <param name="vPointCount">The number of control points of the output surface in the V direction.</param>
        /// <returns>A NURBS surface if successful, or null on failure.</returns>
        public static NurbsSurface CreateFromPlane(Plane plane, Interval uInterval, Interval vInterval, int uDegree, int vDegree, int uPointCount, int vPointCount)
        {
            return ComputeServer.Post<NurbsSurface>(ApiAddress(), plane, uInterval, vInterval, uDegree, vDegree, uPointCount, vPointCount);
        }

        /// <summary>
        /// Computes a discrete spline curve on the surface. In other words, computes a sequence 
        /// of points on the surface, each with a corresponding parameter value.
        /// </summary>
        /// <param name="surface">
        /// The surface on which the curve is constructed. The surface should be G1 continuous. 
        /// If the surface is closed in the u or v direction and is G1 at the seam, the
        /// function will construct point sequences that cross over the seam.
        /// </param>
        /// <param name="fixedPoints">Surface points to interpolate given by parameters. These must be distinct.</param>
        /// <param name="tolerance">Relative tolerance used by the solver. When in doubt, use a tolerance of 0.0.</param>
        /// <param name="periodic">When true constructs a smoothly closed curve.</param>
        /// <param name="initCount">Maximum number of points to insert between fixed points on the first level.</param>
        /// <param name="levels">The number of levels (between 1 and 3) to be used in multi-level solver. Use 1 for single level solve.</param>
        /// <returns>
        /// A sequence of surface points, given by surface parameters, if successful.
        /// The number of output points is approximately: 2 ^ (level-1) * initCount * fixedPoints.Count.
        /// </returns>
        /// <remarks>
        /// To create a curve from the output points, use Surface.CreateCurveOnSurface.
        /// </remarks>
        public static Point2d[] CreateCurveOnSurfacePoints(Surface surface, IEnumerable<Point2d> fixedPoints, double tolerance, bool periodic, int initCount, int levels)
        {
            return ComputeServer.Post<Point2d[]>(ApiAddress(), surface, fixedPoints, tolerance, periodic, initCount, levels);
        }

        /// <summary>
        /// Computes a discrete spline curve on the surface. In other words, computes a sequence 
        /// of points on the surface, each with a corresponding parameter value.
        /// </summary>
        /// <param name="surface">
        /// The surface on which the curve is constructed. The surface should be G1 continuous. 
        /// If the surface is closed in the u or v direction and is G1 at the seam, the
        /// function will construct point sequences that cross over the seam.
        /// </param>
        /// <param name="fixedPoints">Surface points to interpolate given by parameters. These must be distinct.</param>
        /// <param name="tolerance">Relative tolerance used by the solver. When in doubt, use a tolerance of 0.0.</param>
        /// <param name="periodic">When true constructs a smoothly closed curve.</param>
        /// <param name="initCount">Maximum number of points to insert between fixed points on the first level.</param>
        /// <param name="levels">The number of levels (between 1 and 3) to be used in multi-level solver. Use 1 for single level solve.</param>
        /// <returns>
        /// A sequence of surface points, given by surface parameters, if successful.
        /// The number of output points is approximately: 2 ^ (level-1) * initCount * fixedPoints.Count.
        /// </returns>
        /// <remarks>
        /// To create a curve from the output points, use Surface.CreateCurveOnSurface.
        /// </remarks>
        public static Point2d[] CreateCurveOnSurfacePoints(Remote<Surface> surface, IEnumerable<Point2d> fixedPoints, double tolerance, bool periodic, int initCount, int levels)
        {
            return ComputeServer.Post<Point2d[]>(ApiAddress(), surface, fixedPoints, tolerance, periodic, initCount, levels);
        }

        /// <summary>
        /// Fit a sequence of 2d points on a surface to make a curve on the surface.
        /// </summary>
        /// <param name="surface">Surface on which to construct curve.</param>
        /// <param name="points">Parameter space coordinates of the points to interpolate.</param>
        /// <param name="tolerance">Curve should be within tolerance of surface and points.</param>
        /// <param name="periodic">When true make a periodic curve.</param>
        /// <returns>A curve interpolating the points if successful, null on error.</returns>
        /// <remarks>
        /// To produce the input points, use Surface.CreateCurveOnSurfacePoints.
        /// </remarks>
        public static NurbsCurve CreateCurveOnSurface(Surface surface, IEnumerable<Point2d> points, double tolerance, bool periodic)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), surface, points, tolerance, periodic);
        }

        /// <summary>
        /// Fit a sequence of 2d points on a surface to make a curve on the surface.
        /// </summary>
        /// <param name="surface">Surface on which to construct curve.</param>
        /// <param name="points">Parameter space coordinates of the points to interpolate.</param>
        /// <param name="tolerance">Curve should be within tolerance of surface and points.</param>
        /// <param name="periodic">When true make a periodic curve.</param>
        /// <returns>A curve interpolating the points if successful, null on error.</returns>
        /// <remarks>
        /// To produce the input points, use Surface.CreateCurveOnSurfacePoints.
        /// </remarks>
        public static NurbsCurve CreateCurveOnSurface(Remote<Surface> surface, IEnumerable<Point2d> points, double tolerance, bool periodic)
        {
            return ComputeServer.Post<NurbsCurve>(ApiAddress(), surface, points, tolerance, periodic);
        }

        /// <summary>
        /// For expert use only. Makes a pair of compatible NURBS surfaces based on two input surfaces.
        /// </summary>
        /// <param name="surface0">The first surface.</param>
        /// <param name="surface1">The second surface.</param>
        /// <param name="nurb0">The first output NURBS surface.</param>
        /// <param name="nurb1">The second output NURBS surface.</param>
        /// <returns>true if successful, false on failure.</returns>
        public static bool MakeCompatible(Surface surface0, Surface surface1, out NurbsSurface nurb0, out NurbsSurface nurb1)
        {
            return ComputeServer.Post<bool, NurbsSurface, NurbsSurface>(ApiAddress(), out nurb0, out nurb1, surface0, surface1);
        }

        /// <summary>
        /// For expert use only. Makes a pair of compatible NURBS surfaces based on two input surfaces.
        /// </summary>
        /// <param name="surface0">The first surface.</param>
        /// <param name="surface1">The second surface.</param>
        /// <param name="nurb0">The first output NURBS surface.</param>
        /// <param name="nurb1">The second output NURBS surface.</param>
        /// <returns>true if successful, false on failure.</returns>
        public static bool MakeCompatible(Remote<Surface> surface0, Remote<Surface> surface1, Remote<NurbsSurface> nurb0, Remote<NurbsSurface> nurb1)
        {
            return ComputeServer.Post<bool, NurbsSurface, NurbsSurface>(ApiAddress(), out nurb0, out nurb1, surface0, surface1);
        }

        /// <summary>
        /// Constructs a NURBS surface from a 2D grid of control points.
        /// </summary>
        /// <param name="points">Control point locations.</param>
        /// <param name="uCount">Number of points in U direction.</param>
        /// <param name="vCount">Number of points in V direction.</param>
        /// <param name="uDegree">Degree of surface in U direction.</param>
        /// <param name="vDegree">Degree of surface in V direction.</param>
        /// <returns>A NurbsSurface on success or null on failure.</returns>
        /// <remarks>uCount multiplied by vCount must equal the number of points supplied.</remarks>
        public static NurbsSurface CreateFromPoints(IEnumerable<Point3d> points, int uCount, int vCount, int uDegree, int vDegree)
        {
            return ComputeServer.Post<NurbsSurface>(ApiAddress(), points, uCount, vCount, uDegree, vDegree);
        }

        /// <summary>
        /// Constructs a NURBS surface from a 2D grid of points.
        /// </summary>
        /// <param name="points">Control point locations.</param>
        /// <param name="uCount">Number of points in U direction.</param>
        /// <param name="vCount">Number of points in V direction.</param>
        /// <param name="uDegree">Degree of surface in U direction.</param>
        /// <param name="vDegree">Degree of surface in V direction.</param>
        /// <param name="uClosed">true if the surface should be closed in the U direction.</param>
        /// <param name="vClosed">true if the surface should be closed in the V direction.</param>
        /// <returns>A NurbsSurface on success or null on failure.</returns>
        /// <remarks>uCount multiplied by vCount must equal the number of points supplied.</remarks>
        public static NurbsSurface CreateThroughPoints(IEnumerable<Point3d> points, int uCount, int vCount, int uDegree, int vDegree, bool uClosed, bool vClosed)
        {
            return ComputeServer.Post<NurbsSurface>(ApiAddress(), points, uCount, vCount, uDegree, vDegree, uClosed, vClosed);
        }

        /// <summary>
        /// Makes a surface from 4 corner points.
        /// <para>This is the same as calling <see cref="CreateFromCorners(Point3d,Point3d,Point3d,Point3d,double)"/> with tolerance 0.</para>
        /// </summary>
        /// <param name="corner1">The first corner.</param>
        /// <param name="corner2">The second corner.</param>
        /// <param name="corner3">The third corner.</param>
        /// <param name="corner4">The fourth corner.</param>
        /// <returns>the resulting surface or null on error.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_srfpt.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_srfpt.cs' lang='cs'/>
        /// <code source='examples\py\ex_srfpt.py' lang='py'/>
        /// </example>
        public static NurbsSurface CreateFromCorners(Point3d corner1, Point3d corner2, Point3d corner3, Point3d corner4)
        {
            return ComputeServer.Post<NurbsSurface>(ApiAddress(), corner1, corner2, corner3, corner4);
        }

        /// <summary>
        /// Makes a surface from 4 corner points.
        /// </summary>
        /// <param name="corner1">The first corner.</param>
        /// <param name="corner2">The second corner.</param>
        /// <param name="corner3">The third corner.</param>
        /// <param name="corner4">The fourth corner.</param>
        /// <param name="tolerance">Minimum edge length without collapsing to a singularity.</param>
        /// <returns>The resulting surface or null on error.</returns>
        public static NurbsSurface CreateFromCorners(Point3d corner1, Point3d corner2, Point3d corner3, Point3d corner4, double tolerance)
        {
            return ComputeServer.Post<NurbsSurface>(ApiAddress(), corner1, corner2, corner3, corner4, tolerance);
        }

        /// <summary>
        /// Makes a surface from 3 corner points.
        /// </summary>
        /// <param name="corner1">The first corner.</param>
        /// <param name="corner2">The second corner.</param>
        /// <param name="corner3">The third corner.</param>
        /// <returns>The resulting surface or null on error.</returns>
        public static NurbsSurface CreateFromCorners(Point3d corner1, Point3d corner2, Point3d corner3)
        {
            return ComputeServer.Post<NurbsSurface>(ApiAddress(), corner1, corner2, corner3);
        }

        /// <summary>
        /// Constructs a railed Surface-of-Revolution.
        /// </summary>
        /// <param name="profile">Profile curve for revolution.</param>
        /// <param name="rail">Rail curve for revolution.</param>
        /// <param name="axis">Axis of revolution.</param>
        /// <param name="scaleHeight">If true, surface will be locally scaled.</param>
        /// <returns>A NurbsSurface or null on failure.</returns>
        public static NurbsSurface CreateRailRevolvedSurface(Curve profile, Curve rail, Line axis, bool scaleHeight)
        {
            return ComputeServer.Post<NurbsSurface>(ApiAddress(), profile, rail, axis, scaleHeight);
        }

        /// <summary>
        /// Constructs a railed Surface-of-Revolution.
        /// </summary>
        /// <param name="profile">Profile curve for revolution.</param>
        /// <param name="rail">Rail curve for revolution.</param>
        /// <param name="axis">Axis of revolution.</param>
        /// <param name="scaleHeight">If true, surface will be locally scaled.</param>
        /// <returns>A NurbsSurface or null on failure.</returns>
        public static NurbsSurface CreateRailRevolvedSurface(Remote<Curve> profile, Remote<Curve> rail, Line axis, bool scaleHeight)
        {
            return ComputeServer.Post<NurbsSurface>(ApiAddress(), profile, rail, axis, scaleHeight);
        }

        /// <summary>
        /// Builds a surface from an ordered network of curves/edges.
        /// </summary>
        /// <param name="uCurves">An array, a list or any enumerable set of U curves.</param>
        /// <param name="uContinuityStart">
        /// continuity at first U segment, 0 = loose, 1 = position, 2 = tan, 3 = curvature.
        /// </param>
        /// <param name="uContinuityEnd">
        /// continuity at last U segment, 0 = loose, 1 = position, 2 = tan, 3 = curvature.
        /// </param>
        /// <param name="vCurves">An array, a list or any enumerable set of V curves.</param>
        /// <param name="vContinuityStart">
        /// continuity at first V segment, 0 = loose, 1 = position, 2 = tan, 3 = curvature.
        /// </param>
        /// <param name="vContinuityEnd">
        /// continuity at last V segment, 0 = loose, 1 = position, 2 = tan, 3 = curvature.
        /// </param>
        /// <param name="edgeTolerance">tolerance to use along network surface edge.</param>
        /// <param name="interiorTolerance">tolerance to use for the interior curves.</param>
        /// <param name="angleTolerance">angle tolerance to use.</param>
        /// <param name="error">
        /// If the NurbsSurface could not be created, the error value describes where
        /// the failure occurred.  0 = success,  1 = curve sorter failed, 2 = network initializing failed,
        /// 3 = failed to build surface, 4 = network surface is not valid.
        /// </param>
        /// <returns>A NurbsSurface or null on failure.</returns>
        public static NurbsSurface CreateNetworkSurface(IEnumerable<Curve> uCurves, int uContinuityStart, int uContinuityEnd, IEnumerable<Curve> vCurves, int vContinuityStart, int vContinuityEnd, double edgeTolerance, double interiorTolerance, double angleTolerance, out int error)
        {
            return ComputeServer.Post<NurbsSurface, int>(ApiAddress(), out error, uCurves, uContinuityStart, uContinuityEnd, vCurves, vContinuityStart, vContinuityEnd, edgeTolerance, interiorTolerance, angleTolerance);
        }

        /// <summary>
        /// Builds a surface from an ordered network of curves/edges.
        /// </summary>
        /// <param name="uCurves">An array, a list or any enumerable set of U curves.</param>
        /// <param name="uContinuityStart">
        /// continuity at first U segment, 0 = loose, 1 = position, 2 = tan, 3 = curvature.
        /// </param>
        /// <param name="uContinuityEnd">
        /// continuity at last U segment, 0 = loose, 1 = position, 2 = tan, 3 = curvature.
        /// </param>
        /// <param name="vCurves">An array, a list or any enumerable set of V curves.</param>
        /// <param name="vContinuityStart">
        /// continuity at first V segment, 0 = loose, 1 = position, 2 = tan, 3 = curvature.
        /// </param>
        /// <param name="vContinuityEnd">
        /// continuity at last V segment, 0 = loose, 1 = position, 2 = tan, 3 = curvature.
        /// </param>
        /// <param name="edgeTolerance">tolerance to use along network surface edge.</param>
        /// <param name="interiorTolerance">tolerance to use for the interior curves.</param>
        /// <param name="angleTolerance">angle tolerance to use.</param>
        /// <param name="error">
        /// If the NurbsSurface could not be created, the error value describes where
        /// the failure occurred.  0 = success,  1 = curve sorter failed, 2 = network initializing failed,
        /// 3 = failed to build surface, 4 = network surface is not valid.
        /// </param>
        /// <returns>A NurbsSurface or null on failure.</returns>
        public static NurbsSurface CreateNetworkSurface(Remote<IEnumerable<Curve>> uCurves, int uContinuityStart, int uContinuityEnd, Remote<IEnumerable<Curve>> vCurves, int vContinuityStart, int vContinuityEnd, double edgeTolerance, double interiorTolerance, double angleTolerance, out int error)
        {
            return ComputeServer.Post<NurbsSurface, int>(ApiAddress(), out error, uCurves, uContinuityStart, uContinuityEnd, vCurves, vContinuityStart, vContinuityEnd, edgeTolerance, interiorTolerance, angleTolerance);
        }

        /// <summary>
        /// Builds a surface from an auto-sorted network of curves/edges.
        /// </summary>
        /// <param name="curves">An array, a list or any enumerable set of curves/edges, sorted automatically into U and V curves.</param>
        /// <param name="continuity">continuity along edges, 0 = loose, 1 = position, 2 = tan, 3 = curvature.</param>
        /// <param name="edgeTolerance">tolerance to use along network surface edge.</param>
        /// <param name="interiorTolerance">tolerance to use for the interior curves.</param>
        /// <param name="angleTolerance">angle tolerance to use.</param>
        /// <param name="error">
        /// If the NurbsSurface could not be created, the error value describes where
        /// the failure occurred.  0 = success,  1 = curve sorter failed, 2 = network initializing failed,
        /// 3 = failed to build surface, 4 = network surface is not valid.
        /// </param>
        /// <returns>A NurbsSurface or null on failure.</returns>
        public static NurbsSurface CreateNetworkSurface(IEnumerable<Curve> curves, int continuity, double edgeTolerance, double interiorTolerance, double angleTolerance, out int error)
        {
            return ComputeServer.Post<NurbsSurface, int>(ApiAddress(), out error, curves, continuity, edgeTolerance, interiorTolerance, angleTolerance);
        }

        /// <summary>
        /// Builds a surface from an auto-sorted network of curves/edges.
        /// </summary>
        /// <param name="curves">An array, a list or any enumerable set of curves/edges, sorted automatically into U and V curves.</param>
        /// <param name="continuity">continuity along edges, 0 = loose, 1 = position, 2 = tan, 3 = curvature.</param>
        /// <param name="edgeTolerance">tolerance to use along network surface edge.</param>
        /// <param name="interiorTolerance">tolerance to use for the interior curves.</param>
        /// <param name="angleTolerance">angle tolerance to use.</param>
        /// <param name="error">
        /// If the NurbsSurface could not be created, the error value describes where
        /// the failure occurred.  0 = success,  1 = curve sorter failed, 2 = network initializing failed,
        /// 3 = failed to build surface, 4 = network surface is not valid.
        /// </param>
        /// <returns>A NurbsSurface or null on failure.</returns>
        public static NurbsSurface CreateNetworkSurface(Remote<IEnumerable<Curve>> curves, int continuity, double edgeTolerance, double interiorTolerance, double angleTolerance, out int error)
        {
            return ComputeServer.Post<NurbsSurface, int>(ApiAddress(), out error, curves, continuity, edgeTolerance, interiorTolerance, angleTolerance);
        }
    }

    public static class IntersectionCompute
    {
        static string ApiAddress([CallerMemberName] string caller = null)
        {
            return ComputeServer.ApiAddress(typeof(Intersection), caller);
        }

        /// <summary>
        /// Intersects a curve with an (infinite) plane.
        /// </summary>
        /// <param name="curve">Curve to intersect.</param>
        /// <param name="plane">Plane to intersect with.</param>
        /// <param name="tolerance">Tolerance to use during intersection.</param>
        /// <returns>A list of intersection events or null if no intersections were recorded.</returns>
        public static CurveIntersections CurvePlane(Curve curve, Plane plane, double tolerance)
        {
            return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, plane, tolerance);
        }

        /// <summary>
        /// Intersects a curve with an (infinite) plane.
        /// </summary>
        /// <param name="curve">Curve to intersect.</param>
        /// <param name="plane">Plane to intersect with.</param>
        /// <param name="tolerance">Tolerance to use during intersection.</param>
        /// <returns>A list of intersection events or null if no intersections were recorded.</returns>
        public static CurveIntersections CurvePlane(Remote<Curve> curve, Plane plane, double tolerance)
        {
            return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, plane, tolerance);
        }

        /// <summary>
        /// Intersects a mesh with an (infinite) plane.
        /// </summary>
        /// <param name="mesh">Mesh to intersect.</param>
        /// <param name="plane">Plane to intersect with.</param>
        /// <returns>An array of polylines describing the intersection loops or null (Nothing in Visual Basic) if no intersections could be found.</returns>
        public static Polyline[] MeshPlane(Mesh mesh, Plane plane)
        {
            return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh, plane);
        }

        /// <summary>
        /// Intersects a mesh with an (infinite) plane.
        /// </summary>
        /// <param name="mesh">Mesh to intersect.</param>
        /// <param name="plane">Plane to intersect with.</param>
        /// <returns>An array of polylines describing the intersection loops or null (Nothing in Visual Basic) if no intersections could be found.</returns>
        public static Polyline[] MeshPlane(Remote<Mesh> mesh, Plane plane)
        {
            return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh, plane);
        }

        /// <summary>
        /// Intersects a mesh with a collection of (infinite) planes.
        /// </summary>
        /// <param name="mesh">Mesh to intersect.</param>
        /// <param name="planes">Planes to intersect with.</param>
        /// <returns>An array of polylines describing the intersection loops or null (Nothing in Visual Basic) if no intersections could be found.</returns>
        /// <exception cref="ArgumentNullException">If planes is null.</exception>
        public static Polyline[] MeshPlane(Mesh mesh, IEnumerable<Plane> planes)
        {
            return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh, planes);
        }

        /// <summary>
        /// Intersects a mesh with a collection of (infinite) planes.
        /// </summary>
        /// <param name="mesh">Mesh to intersect.</param>
        /// <param name="planes">Planes to intersect with.</param>
        /// <returns>An array of polylines describing the intersection loops or null (Nothing in Visual Basic) if no intersections could be found.</returns>
        /// <exception cref="ArgumentNullException">If planes is null.</exception>
        public static Polyline[] MeshPlane(Remote<Mesh> mesh, IEnumerable<Plane> planes)
        {
            return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh, planes);
        }

        /// <summary>
        /// Intersects a Brep with an (infinite) plane.
        /// </summary>
        /// <param name="brep">Brep to intersect.</param>
        /// <param name="plane">Plane to intersect with.</param>
        /// <param name="tolerance">Tolerance to use for intersections.</param>
        /// <param name="intersectionCurves">The intersection curves will be returned here.</param>
        /// <param name="intersectionPoints">The intersection points will be returned here.</param>
        /// <returns>true on success, false on failure.</returns>
        public static bool BrepPlane(Brep brep, Plane plane, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
        {
            return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, brep, plane, tolerance);
        }

        /// <summary>
        /// Intersects a Brep with an (infinite) plane.
        /// </summary>
        /// <param name="brep">Brep to intersect.</param>
        /// <param name="plane">Plane to intersect with.</param>
        /// <param name="tolerance">Tolerance to use for intersections.</param>
        /// <param name="intersectionCurves">The intersection curves will be returned here.</param>
        /// <param name="intersectionPoints">The intersection points will be returned here.</param>
        /// <returns>true on success, false on failure.</returns>
        public static bool BrepPlane(Remote<Brep> brep, Plane plane, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
        {
            return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, brep, plane, tolerance);
        }

        /// <summary>
        /// Finds the places where a curve intersects itself. 
        /// </summary>
        /// <param name="curve">Curve for self-intersections.</param>
        /// <param name="tolerance">Intersection tolerance. If the curve approaches itself to within tolerance, 
        /// an intersection is assumed.</param>
        /// <returns>A collection of intersection events.</returns>
        public static CurveIntersections CurveSelf(Curve curve, double tolerance)
        {
            return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, tolerance);
        }

        /// <summary>
        /// Finds the places where a curve intersects itself. 
        /// </summary>
        /// <param name="curve">Curve for self-intersections.</param>
        /// <param name="tolerance">Intersection tolerance. If the curve approaches itself to within tolerance, 
        /// an intersection is assumed.</param>
        /// <returns>A collection of intersection events.</returns>
        public static CurveIntersections CurveSelf(Remote<Curve> curve, double tolerance)
        {
            return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, tolerance);
        }

        /// <summary>
        /// Finds the intersections between two curves. 
        /// </summary>
        /// <param name="curveA">First curve for intersection.</param>
        /// <param name="curveB">Second curve for intersection.</param>
        /// <param name="tolerance">Intersection tolerance. If the curves approach each other to within tolerance, an intersection is assumed.</param>
        /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
        /// <returns>A collection of intersection events.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_intersectcurves.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_intersectcurves.cs' lang='cs'/>
        /// <code source='examples\py\ex_intersectcurves.py' lang='py'/>
        /// </example>
        public static CurveIntersections CurveCurve(Curve curveA, Curve curveB, double tolerance, double overlapTolerance)
        {
            return ComputeServer.Post<CurveIntersections>(ApiAddress(), curveA, curveB, tolerance, overlapTolerance);
        }

        /// <summary>
        /// Finds the intersections between two curves. 
        /// </summary>
        /// <param name="curveA">First curve for intersection.</param>
        /// <param name="curveB">Second curve for intersection.</param>
        /// <param name="tolerance">Intersection tolerance. If the curves approach each other to within tolerance, an intersection is assumed.</param>
        /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
        /// <returns>A collection of intersection events.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_intersectcurves.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_intersectcurves.cs' lang='cs'/>
        /// <code source='examples\py\ex_intersectcurves.py' lang='py'/>
        /// </example>
        public static CurveIntersections CurveCurve(Remote<Curve> curveA, Remote<Curve> curveB, double tolerance, double overlapTolerance)
        {
            return ComputeServer.Post<CurveIntersections>(ApiAddress(), curveA, curveB, tolerance, overlapTolerance);
        }

        /// <summary>
        /// Finds the intersections between two curves.
        /// </summary>
        /// <param name="curveA">First curve for intersection.</param>
        /// <param name="curveB">Second curve for intersection.</param>
        /// <param name="tolerance">Intersection tolerance. If the curves approach each other to within tolerance, an intersection is assumed.</param>
        /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
        /// <param name="invalidIndices">The indices in the resulting CurveIntersections collection that are invalid.</param>
        /// <param name="textLog">A text log that contains tails about the invalid intersection events.</param>
        /// <returns>A collection of intersection events.</returns>
        public static CurveIntersections CurveCurveValidate(Curve curveA, Curve curveB, double tolerance, double overlapTolerance, out int[] invalidIndices, out TextLog textLog)
        {
            return ComputeServer.Post<CurveIntersections, int[], TextLog>(ApiAddress(), out invalidIndices, out textLog, curveA, curveB, tolerance, overlapTolerance);
        }

        /// <summary>
        /// Finds the intersections between two curves.
        /// </summary>
        /// <param name="curveA">First curve for intersection.</param>
        /// <param name="curveB">Second curve for intersection.</param>
        /// <param name="tolerance">Intersection tolerance. If the curves approach each other to within tolerance, an intersection is assumed.</param>
        /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
        /// <param name="invalidIndices">The indices in the resulting CurveIntersections collection that are invalid.</param>
        /// <param name="textLog">A text log that contains tails about the invalid intersection events.</param>
        /// <returns>A collection of intersection events.</returns>
        public static CurveIntersections CurveCurveValidate(Remote<Curve> curveA, Remote<Curve> curveB, double tolerance, double overlapTolerance, out int[] invalidIndices, out TextLog textLog)
        {
            return ComputeServer.Post<CurveIntersections, int[], TextLog>(ApiAddress(), out invalidIndices, out textLog, curveA, curveB, tolerance, overlapTolerance);
        }

        /// <summary>
        /// Intersects a curve and an infinite line. 
        /// </summary>
        /// <param name="curve">Curve for intersection.</param>
        /// <param name="line">Infinite line to intersect.</param>
        /// <param name="tolerance">Intersection tolerance. If the curves approach each other to within tolerance, an intersection is assumed.</param>
        /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
        /// <returns>A collection of intersection events.</returns>
        public static CurveIntersections CurveLine(Curve curve, Line line, double tolerance, double overlapTolerance)
        {
            return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, line, tolerance, overlapTolerance);
        }

        /// <summary>
        /// Intersects a curve and an infinite line. 
        /// </summary>
        /// <param name="curve">Curve for intersection.</param>
        /// <param name="line">Infinite line to intersect.</param>
        /// <param name="tolerance">Intersection tolerance. If the curves approach each other to within tolerance, an intersection is assumed.</param>
        /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
        /// <returns>A collection of intersection events.</returns>
        public static CurveIntersections CurveLine(Remote<Curve> curve, Line line, double tolerance, double overlapTolerance)
        {
            return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, line, tolerance, overlapTolerance);
        }

        /// <summary>
        /// Intersects a curve and a surface.
        /// </summary>
        /// <param name="curve">Curve for intersection.</param>
        /// <param name="surface">Surface for intersection.</param>
        /// <param name="tolerance">Intersection tolerance. If the curve approaches the surface to within tolerance, an intersection is assumed.</param>
        /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
        /// <returns>A collection of intersection events.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_curvesurfaceintersect.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_curvesurfaceintersect.cs' lang='cs'/>
        /// <code source='examples\py\ex_curvesurfaceintersect.py' lang='py'/>
        /// </example>
        public static CurveIntersections CurveSurface(Curve curve, Surface surface, double tolerance, double overlapTolerance)
        {
            return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, surface, tolerance, overlapTolerance);
        }

        /// <summary>
        /// Intersects a curve and a surface.
        /// </summary>
        /// <param name="curve">Curve for intersection.</param>
        /// <param name="surface">Surface for intersection.</param>
        /// <param name="tolerance">Intersection tolerance. If the curve approaches the surface to within tolerance, an intersection is assumed.</param>
        /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
        /// <returns>A collection of intersection events.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_curvesurfaceintersect.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_curvesurfaceintersect.cs' lang='cs'/>
        /// <code source='examples\py\ex_curvesurfaceintersect.py' lang='py'/>
        /// </example>
        public static CurveIntersections CurveSurface(Remote<Curve> curve, Remote<Surface> surface, double tolerance, double overlapTolerance)
        {
            return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, surface, tolerance, overlapTolerance);
        }

        /// <summary>
        /// Intersects a curve and a surface.
        /// </summary>
        /// <param name="curve">Curve for intersection.</param>
        /// <param name="surface">Surface for intersection.</param>
        /// <param name="tolerance">Intersection tolerance. If the curve approaches the surface to within tolerance, an intersection is assumed.</param>
        /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
        /// <param name="invalidIndices">The indices in the resulting CurveIntersections collection that are invalid.</param>
        /// <param name="textLog">A text log that contains tails about the invalid intersection events.</param>
        /// <returns>A collection of intersection events.</returns>
        public static CurveIntersections CurveSurfaceValidate(Curve curve, Surface surface, double tolerance, double overlapTolerance, out int[] invalidIndices, out TextLog textLog)
        {
            return ComputeServer.Post<CurveIntersections, int[], TextLog>(ApiAddress(), out invalidIndices, out textLog, curve, surface, tolerance, overlapTolerance);
        }

        /// <summary>
        /// Intersects a curve and a surface.
        /// </summary>
        /// <param name="curve">Curve for intersection.</param>
        /// <param name="surface">Surface for intersection.</param>
        /// <param name="tolerance">Intersection tolerance. If the curve approaches the surface to within tolerance, an intersection is assumed.</param>
        /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
        /// <param name="invalidIndices">The indices in the resulting CurveIntersections collection that are invalid.</param>
        /// <param name="textLog">A text log that contains tails about the invalid intersection events.</param>
        /// <returns>A collection of intersection events.</returns>
        public static CurveIntersections CurveSurfaceValidate(Remote<Curve> curve, Remote<Surface> surface, double tolerance, double overlapTolerance, out int[] invalidIndices, out TextLog textLog)
        {
            return ComputeServer.Post<CurveIntersections, int[], TextLog>(ApiAddress(), out invalidIndices, out textLog, curve, surface, tolerance, overlapTolerance);
        }

        /// <summary>
        /// Intersects a sub-curve and a surface.
        /// </summary>
        /// <param name="curve">Curve for intersection.</param>
        /// <param name="curveDomain">Domain of sub-curve to take into consideration for Intersections.</param>
        /// <param name="surface">Surface for intersection.</param>
        /// <param name="tolerance">Intersection tolerance. If the curve approaches the surface to within tolerance, an intersection is assumed.</param>
        /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
        /// <returns>A collection of intersection events.</returns>
        public static CurveIntersections CurveSurface(Curve curve, Interval curveDomain, Surface surface, double tolerance, double overlapTolerance)
        {
            return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, curveDomain, surface, tolerance, overlapTolerance);
        }

        /// <summary>
        /// Intersects a sub-curve and a surface.
        /// </summary>
        /// <param name="curve">Curve for intersection.</param>
        /// <param name="curveDomain">Domain of sub-curve to take into consideration for Intersections.</param>
        /// <param name="surface">Surface for intersection.</param>
        /// <param name="tolerance">Intersection tolerance. If the curve approaches the surface to within tolerance, an intersection is assumed.</param>
        /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
        /// <returns>A collection of intersection events.</returns>
        public static CurveIntersections CurveSurface(Remote<Curve> curve, Interval curveDomain, Remote<Surface> surface, double tolerance, double overlapTolerance)
        {
            return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, curveDomain, surface, tolerance, overlapTolerance);
        }

        /// <summary>
        /// Intersects a sub-curve and a surface.
        /// </summary>
        /// <param name="curve">Curve for intersection.</param>
        /// <param name="curveDomain">Domain of sub-curve to take into consideration for Intersections.</param>
        /// <param name="surface">Surface for intersection.</param>
        /// <param name="tolerance">Intersection tolerance. If the curve approaches the surface to within tolerance, an intersection is assumed.</param>
        /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
        /// <param name="invalidIndices">The indices in the resulting CurveIntersections collection that are invalid.</param>
        /// <param name="textLog">A text log that contains tails about the invalid intersection events.</param>
        /// <returns>A collection of intersection events.</returns>
        public static CurveIntersections CurveSurfaceValidate(Curve curve, Interval curveDomain, Surface surface, double tolerance, double overlapTolerance, out int[] invalidIndices, out TextLog textLog)
        {
            return ComputeServer.Post<CurveIntersections, int[], TextLog>(ApiAddress(), out invalidIndices, out textLog, curve, curveDomain, surface, tolerance, overlapTolerance);
        }

        /// <summary>
        /// Intersects a sub-curve and a surface.
        /// </summary>
        /// <param name="curve">Curve for intersection.</param>
        /// <param name="curveDomain">Domain of sub-curve to take into consideration for Intersections.</param>
        /// <param name="surface">Surface for intersection.</param>
        /// <param name="tolerance">Intersection tolerance. If the curve approaches the surface to within tolerance, an intersection is assumed.</param>
        /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
        /// <param name="invalidIndices">The indices in the resulting CurveIntersections collection that are invalid.</param>
        /// <param name="textLog">A text log that contains tails about the invalid intersection events.</param>
        /// <returns>A collection of intersection events.</returns>
        public static CurveIntersections CurveSurfaceValidate(Remote<Curve> curve, Interval curveDomain, Remote<Surface> surface, double tolerance, double overlapTolerance, out int[] invalidIndices, out TextLog textLog)
        {
            return ComputeServer.Post<CurveIntersections, int[], TextLog>(ApiAddress(), out invalidIndices, out textLog, curve, curveDomain, surface, tolerance, overlapTolerance);
        }

        /// <summary>
        /// Intersects a curve with a Brep. This function returns the 3D points of intersection
        /// and 3D overlap curves. If an error occurs while processing overlap curves, this function 
        /// will return false, but it will still provide partial results.
        /// </summary>
        /// <param name="curve">Curve for intersection.</param>
        /// <param name="brep">Brep for intersection.</param>
        /// <param name="tolerance">Fitting and near miss tolerance.</param>
        /// <param name="overlapCurves">The overlap curves will be returned here.</param>
        /// <param name="intersectionPoints">The intersection points will be returned here.</param>
        /// <returns>true on success, false on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_elevation.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_elevation.cs' lang='cs'/>
        /// <code source='examples\py\ex_elevation.py' lang='py'/>
        /// </example>
        public static bool CurveBrep(Curve curve, Brep brep, double tolerance, out Curve[] overlapCurves, out Point3d[] intersectionPoints)
        {
            return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out overlapCurves, out intersectionPoints, curve, brep, tolerance);
        }

        /// <summary>
        /// Intersects a curve with a Brep. This function returns the 3D points of intersection
        /// and 3D overlap curves. If an error occurs while processing overlap curves, this function 
        /// will return false, but it will still provide partial results.
        /// </summary>
        /// <param name="curve">Curve for intersection.</param>
        /// <param name="brep">Brep for intersection.</param>
        /// <param name="tolerance">Fitting and near miss tolerance.</param>
        /// <param name="overlapCurves">The overlap curves will be returned here.</param>
        /// <param name="intersectionPoints">The intersection points will be returned here.</param>
        /// <returns>true on success, false on failure.</returns>
        /// <example>
        /// <code source='examples\vbnet\ex_elevation.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_elevation.cs' lang='cs'/>
        /// <code source='examples\py\ex_elevation.py' lang='py'/>
        /// </example>
        public static bool CurveBrep(Remote<Curve> curve, Remote<Brep> brep, double tolerance, out Curve[] overlapCurves, out Point3d[] intersectionPoints)
        {
            return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out overlapCurves, out intersectionPoints, curve, brep, tolerance);
        }

        /// <summary>
        /// Intersect a curve with a Brep. This function returns the intersection parameters on the curve.
        /// </summary>
        /// <param name="curve">Curve.</param>
        /// <param name="brep">Brep.</param>
        /// <param name="tolerance">Absolute tolerance for intersections.</param>
        /// <param name="angleTolerance">Angle tolerance in radians.</param>
        /// <param name="t">Curve parameters at intersections.</param>
        /// <returns>True on success, false on failure.</returns>
        public static bool CurveBrep(Curve curve, Brep brep, double tolerance, double angleTolerance, out double[] t)
        {
            return ComputeServer.Post<bool, double[]>(ApiAddress(), out t, curve, brep, tolerance, angleTolerance);
        }

        /// <summary>
        /// Intersect a curve with a Brep. This function returns the intersection parameters on the curve.
        /// </summary>
        /// <param name="curve">Curve.</param>
        /// <param name="brep">Brep.</param>
        /// <param name="tolerance">Absolute tolerance for intersections.</param>
        /// <param name="angleTolerance">Angle tolerance in radians.</param>
        /// <param name="t">Curve parameters at intersections.</param>
        /// <returns>True on success, false on failure.</returns>
        public static bool CurveBrep(Remote<Curve> curve, Remote<Brep> brep, double tolerance, double angleTolerance, out double[] t)
        {
            return ComputeServer.Post<bool, double[]>(ApiAddress(), out t, curve, brep, tolerance, angleTolerance);
        }

        /// <summary>
        /// Intersects a curve with a Brep face.
        /// </summary>
        /// <param name="curve">A curve.</param>
        /// <param name="face">A brep face.</param>
        /// <param name="tolerance">Fitting and near miss tolerance.</param>
        /// <param name="overlapCurves">A overlap curves array argument. This out reference is assigned during the call.</param>
        /// <param name="intersectionPoints">A points array argument. This out reference is assigned during the call.</param>
        /// <returns>true on success, false on failure.</returns>
        public static bool CurveBrepFace(Curve curve, BrepFace face, double tolerance, out Curve[] overlapCurves, out Point3d[] intersectionPoints)
        {
            return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out overlapCurves, out intersectionPoints, curve, face, tolerance);
        }

        /// <summary>
        /// Intersects a curve with a Brep face.
        /// </summary>
        /// <param name="curve">A curve.</param>
        /// <param name="face">A brep face.</param>
        /// <param name="tolerance">Fitting and near miss tolerance.</param>
        /// <param name="overlapCurves">A overlap curves array argument. This out reference is assigned during the call.</param>
        /// <param name="intersectionPoints">A points array argument. This out reference is assigned during the call.</param>
        /// <returns>true on success, false on failure.</returns>
        public static bool CurveBrepFace(Remote<Curve> curve, BrepFace face, double tolerance, out Curve[] overlapCurves, out Point3d[] intersectionPoints)
        {
            return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out overlapCurves, out intersectionPoints, curve, face, tolerance);
        }

        /// <summary>
        /// Intersects two Surfaces.
        /// </summary>
        /// <param name="surfaceA">First Surface for intersection.</param>
        /// <param name="surfaceB">Second Surface for intersection.</param>
        /// <param name="tolerance">Intersection tolerance.</param>
        /// <param name="intersectionCurves">The intersection curves will be returned here.</param>
        /// <param name="intersectionPoints">The intersection points will be returned here.</param>
        /// <returns>true on success, false on failure.</returns>
        public static bool SurfaceSurface(Surface surfaceA, Surface surfaceB, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
        {
            return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, surfaceA, surfaceB, tolerance);
        }

        /// <summary>
        /// Intersects two Surfaces.
        /// </summary>
        /// <param name="surfaceA">First Surface for intersection.</param>
        /// <param name="surfaceB">Second Surface for intersection.</param>
        /// <param name="tolerance">Intersection tolerance.</param>
        /// <param name="intersectionCurves">The intersection curves will be returned here.</param>
        /// <param name="intersectionPoints">The intersection points will be returned here.</param>
        /// <returns>true on success, false on failure.</returns>
        public static bool SurfaceSurface(Remote<Surface> surfaceA, Remote<Surface> surfaceB, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
        {
            return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, surfaceA, surfaceB, tolerance);
        }

        /// <summary>
        /// Intersects two Breps.
        /// </summary>
        /// <param name="brepA">First Brep for intersection.</param>
        /// <param name="brepB">Second Brep for intersection.</param>
        /// <param name="tolerance">Intersection tolerance.</param>
        /// <param name="intersectionCurves">The intersection curves will be returned here.</param>
        /// <param name="intersectionPoints">The intersection points will be returned here.</param>
        /// <returns>true on success; false on failure.</returns>
        public static bool BrepBrep(Brep brepA, Brep brepB, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
        {
            return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, brepA, brepB, tolerance);
        }

        /// <summary>
        /// Intersects two Breps.
        /// </summary>
        /// <param name="brepA">First Brep for intersection.</param>
        /// <param name="brepB">Second Brep for intersection.</param>
        /// <param name="tolerance">Intersection tolerance.</param>
        /// <param name="intersectionCurves">The intersection curves will be returned here.</param>
        /// <param name="intersectionPoints">The intersection points will be returned here.</param>
        /// <returns>true on success; false on failure.</returns>
        public static bool BrepBrep(Remote<Brep> brepA, Remote<Brep> brepB, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
        {
            return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, brepA, brepB, tolerance);
        }

        /// <summary>
        /// Intersects a Brep and a Surface.
        /// </summary>
        /// <param name="brep">A brep to be intersected.</param>
        /// <param name="surface">A surface to be intersected.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <param name="intersectionCurves">The intersection curves array argument. This out reference is assigned during the call.</param>
        /// <param name="intersectionPoints">The intersection points array argument. This out reference is assigned during the call.</param>
        /// <returns>true on success; false on failure.</returns>
        public static bool BrepSurface(Brep brep, Surface surface, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
        {
            return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, brep, surface, tolerance);
        }

        /// <summary>
        /// Intersects a Brep and a Surface.
        /// </summary>
        /// <param name="brep">A brep to be intersected.</param>
        /// <param name="surface">A surface to be intersected.</param>
        /// <param name="tolerance">A tolerance value.</param>
        /// <param name="intersectionCurves">The intersection curves array argument. This out reference is assigned during the call.</param>
        /// <param name="intersectionPoints">The intersection points array argument. This out reference is assigned during the call.</param>
        /// <returns>true on success; false on failure.</returns>
        public static bool BrepSurface(Remote<Brep> brep, Remote<Surface> surface, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
        {
            return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, brep, surface, tolerance);
        }

        /// <summary>
        /// This is an old overload kept for compatibility. Overlaps and near misses are ignored.
        /// </summary>
        /// <param name="meshA">First mesh for intersection.</param>
        /// <param name="meshB">Second mesh for intersection.</param>
        /// <returns>An array of intersection line segments, or null if no intersections were found.</returns>
        /// <deprecated>7.0</deprecated>
        public static Line[] MeshMeshFast(Mesh meshA, Mesh meshB)
        {
            return ComputeServer.Post<Line[]>(ApiAddress(), meshA, meshB);
        }

        /// <summary>
        /// This is an old overload kept for compatibility. Overlaps and near misses are ignored.
        /// </summary>
        /// <param name="meshA">First mesh for intersection.</param>
        /// <param name="meshB">Second mesh for intersection.</param>
        /// <returns>An array of intersection line segments, or null if no intersections were found.</returns>
        /// <deprecated>7.0</deprecated>
        public static Line[] MeshMeshFast(Remote<Mesh> meshA, Remote<Mesh> meshB)
        {
            return ComputeServer.Post<Line[]>(ApiAddress(), meshA, meshB);
        }

        /// <summary>
        /// Intersects two meshes. Overlaps and near misses are handled. This is an old method kept for compatibility.
        /// </summary>
        /// <param name="meshA">First mesh for intersection.</param>
        /// <param name="meshB">Second mesh for intersection.</param>
        /// <param name="tolerance">A tolerance value. If negative, the positive value will be used.
        /// WARNING! Good tolerance values are in the magnitude of 10^-7, or RhinoMath.SqrtEpsilon*10.</param>
        /// <returns>An array of intersection and overlaps polylines.</returns>
        public static Polyline[] MeshMeshAccurate(Mesh meshA, Mesh meshB, double tolerance)
        {
            return ComputeServer.Post<Polyline[]>(ApiAddress(), meshA, meshB, tolerance);
        }

        /// <summary>
        /// Intersects two meshes. Overlaps and near misses are handled. This is an old method kept for compatibility.
        /// </summary>
        /// <param name="meshA">First mesh for intersection.</param>
        /// <param name="meshB">Second mesh for intersection.</param>
        /// <param name="tolerance">A tolerance value. If negative, the positive value will be used.
        /// WARNING! Good tolerance values are in the magnitude of 10^-7, or RhinoMath.SqrtEpsilon*10.</param>
        /// <returns>An array of intersection and overlaps polylines.</returns>
        public static Polyline[] MeshMeshAccurate(Remote<Mesh> meshA, Remote<Mesh> meshB, double tolerance)
        {
            return ComputeServer.Post<Polyline[]>(ApiAddress(), meshA, meshB, tolerance);
        }

        /// <summary>Finds the first intersection of a ray with a mesh.</summary>
        /// <param name="mesh">A mesh to intersect.</param>
        /// <param name="ray">A ray to be casted.</param>
        /// <returns>
        /// >= 0.0 parameter along ray if successful.
        /// &lt; 0.0 if no intersection found.
        /// </returns>
        public static double MeshRay(Mesh mesh, Ray3d ray)
        {
            return ComputeServer.Post<double>(ApiAddress(), mesh, ray);
        }

        /// <summary>Finds the first intersection of a ray with a mesh.</summary>
        /// <param name="mesh">A mesh to intersect.</param>
        /// <param name="ray">A ray to be casted.</param>
        /// <returns>
        /// >= 0.0 parameter along ray if successful.
        /// &lt; 0.0 if no intersection found.
        /// </returns>
        public static double MeshRay(Remote<Mesh> mesh, Ray3d ray)
        {
            return ComputeServer.Post<double>(ApiAddress(), mesh, ray);
        }

        /// <summary>Finds the first intersection of a ray with a mesh.</summary>
        /// <param name="mesh">A mesh to intersect.</param>
        /// <param name="ray">A ray to be casted.</param>
        /// <param name="meshFaceIndices">faces on mesh that ray intersects.</param>
        /// <returns>
        /// >= 0.0 parameter along ray if successful.
        /// &lt; 0.0 if no intersection found.
        /// </returns>
        /// <remarks>
        /// The ray may intersect more than one face in cases where the ray hits
        /// the edge between two faces or the vertex corner shared by multiple faces.
        /// </remarks>
        public static double MeshRay(Mesh mesh, Ray3d ray, out int[] meshFaceIndices)
        {
            return ComputeServer.Post<double, int[]>(ApiAddress(), out meshFaceIndices, mesh, ray);
        }

        /// <summary>Finds the first intersection of a ray with a mesh.</summary>
        /// <param name="mesh">A mesh to intersect.</param>
        /// <param name="ray">A ray to be casted.</param>
        /// <param name="meshFaceIndices">faces on mesh that ray intersects.</param>
        /// <returns>
        /// >= 0.0 parameter along ray if successful.
        /// &lt; 0.0 if no intersection found.
        /// </returns>
        /// <remarks>
        /// The ray may intersect more than one face in cases where the ray hits
        /// the edge between two faces or the vertex corner shared by multiple faces.
        /// </remarks>
        public static double MeshRay(Remote<Mesh> mesh, Ray3d ray, out int[] meshFaceIndices)
        {
            return ComputeServer.Post<double, int[]>(ApiAddress(), out meshFaceIndices, mesh, ray);
        }

        /// <summary>
        /// Finds the intersection of a mesh and a polyline. Points are not guaranteed to be sorted along the polyline.
        /// </summary>
        /// <param name="mesh">A mesh to intersect.</param>
        /// <param name="curve">A polyline curves to intersect.</param>
        /// <param name="faceIds">The indices of the intersecting faces. This out reference is assigned during the call.</param>
        /// <returns>An array of points: one for each face that was passed by the faceIds out reference.</returns>
        public static Point3d[] MeshPolyline(Mesh mesh, PolylineCurve curve, out int[] faceIds)
        {
            return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out faceIds, mesh, curve);
        }

        /// <summary>
        /// Finds the intersection of a mesh and a polyline. Points are not guaranteed to be sorted along the polyline.
        /// </summary>
        /// <param name="mesh">A mesh to intersect.</param>
        /// <param name="curve">A polyline curves to intersect.</param>
        /// <param name="faceIds">The indices of the intersecting faces. This out reference is assigned during the call.</param>
        /// <returns>An array of points: one for each face that was passed by the faceIds out reference.</returns>
        public static Point3d[] MeshPolyline(Remote<Mesh> mesh, PolylineCurve curve, out int[] faceIds)
        {
            return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out faceIds, mesh, curve);
        }

        /// <summary>
        /// Finds the intersection of a mesh and a polyline. Points are guaranteed to be sorted along the polyline.
        /// </summary>
        /// <param name="mesh">A mesh to intersect.</param>
        /// <param name="curve">A polyline curves to intersect.</param>
        /// <param name="faceIds">The indices of the intersecting faces. This out reference is assigned during the call.</param>
        /// <returns>An array of points: one for each face that was passed by the faceIds out reference.</returns>
        public static Point3d[] MeshPolylineSorted(Mesh mesh, PolylineCurve curve, out int[] faceIds)
        {
            return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out faceIds, mesh, curve);
        }

        /// <summary>
        /// Finds the intersection of a mesh and a polyline. Points are guaranteed to be sorted along the polyline.
        /// </summary>
        /// <param name="mesh">A mesh to intersect.</param>
        /// <param name="curve">A polyline curves to intersect.</param>
        /// <param name="faceIds">The indices of the intersecting faces. This out reference is assigned during the call.</param>
        /// <returns>An array of points: one for each face that was passed by the faceIds out reference.</returns>
        public static Point3d[] MeshPolylineSorted(Remote<Mesh> mesh, PolylineCurve curve, out int[] faceIds)
        {
            return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out faceIds, mesh, curve);
        }

        /// <summary>
        /// Finds the intersections of a mesh and a line. The points are not necessarily sorted.
        /// </summary>
        /// <param name="mesh">A mesh to intersect</param>
        /// <param name="line">The line to intersect with the mesh</param>
        /// <param name="faceIds">The indices of the intersecting faces. This out reference is assigned during the call.</param>
        /// <returns>An array of points: one for each face that was passed by the faceIds out reference.</returns>
        public static Point3d[] MeshLine(Mesh mesh, Line line, out int[] faceIds)
        {
            return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out faceIds, mesh, line);
        }

        /// <summary>
        /// Finds the intersections of a mesh and a line. The points are not necessarily sorted.
        /// </summary>
        /// <param name="mesh">A mesh to intersect</param>
        /// <param name="line">The line to intersect with the mesh</param>
        /// <param name="faceIds">The indices of the intersecting faces. This out reference is assigned during the call.</param>
        /// <returns>An array of points: one for each face that was passed by the faceIds out reference.</returns>
        public static Point3d[] MeshLine(Remote<Mesh> mesh, Line line, out int[] faceIds)
        {
            return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out faceIds, mesh, line);
        }

        /// <summary>
        /// Finds the intersections of a mesh and a line. Points are sorted along the line.
        /// </summary>
        /// <param name="mesh">A mesh to intersect</param>
        /// <param name="line">The line to intersect with the mesh</param>
        /// <param name="faceIds">The indices of the intersecting faces. This out reference is assigned during the call.</param>
        /// <returns>An array of points: one for each face that was passed by the faceIds out reference.</returns>
        public static Point3d[] MeshLineSorted(Mesh mesh, Line line, out int[] faceIds)
        {
            return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out faceIds, mesh, line);
        }

        /// <summary>
        /// Finds the intersections of a mesh and a line. Points are sorted along the line.
        /// </summary>
        /// <param name="mesh">A mesh to intersect</param>
        /// <param name="line">The line to intersect with the mesh</param>
        /// <param name="faceIds">The indices of the intersecting faces. This out reference is assigned during the call.</param>
        /// <returns>An array of points: one for each face that was passed by the faceIds out reference.</returns>
        public static Point3d[] MeshLineSorted(Remote<Mesh> mesh, Line line, out int[] faceIds)
        {
            return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out faceIds, mesh, line);
        }

        /// <summary>
        /// Computes point intersections that occur when shooting a ray to a collection of surfaces and Breps.
        /// </summary>
        /// <param name="ray">A ray used in intersection.</param>
        /// <param name="geometry">Only Surface and Brep objects are currently supported. Trims are ignored on Breps.</param>
        /// <param name="maxReflections">The maximum number of reflections. This value should be any value between 1 and 1000, inclusive.</param>
        /// <returns>An array of points: one for each surface or Brep face that was hit, or an empty array on failure.</returns>
        /// <exception cref="ArgumentNullException">geometry is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">maxReflections is strictly outside the [1-1000] range.</exception>
        public static Point3d[] RayShoot(Ray3d ray, IEnumerable<GeometryBase> geometry, int maxReflections)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), ray, geometry, maxReflections);
        }

        /// <summary>
        /// Computes point intersections that occur when shooting a ray to a collection of surfaces and Breps.
        /// </summary>
        /// <param name="ray">A ray used in intersection.</param>
        /// <param name="geometry">Only Surface and Brep objects are currently supported. Trims are ignored on Breps.</param>
        /// <param name="maxReflections">The maximum number of reflections. This value should be any value between 1 and 1000, inclusive.</param>
        /// <returns>An array of points: one for each surface or Brep face that was hit, or an empty array on failure.</returns>
        /// <exception cref="ArgumentNullException">geometry is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">maxReflections is strictly outside the [1-1000] range.</exception>
        public static Point3d[] RayShoot(Ray3d ray, Remote<IEnumerable<GeometryBase>> geometry, int maxReflections)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), ray, geometry, maxReflections);
        }

        /// <summary>
        /// Computes point intersections that occur when shooting a ray to a collection of surfaces and Breps.
        /// </summary>
        /// <param name="geometry">The collection of surfaces and Breps to intersect. Trims are ignored on Breps.</param>
        /// <param name="ray">>A ray used in intersection.</param>
        /// <param name="maxReflections">The maximum number of reflections. This value should be any value between 1 and 1000, inclusive.</param>
        /// <returns>An array of RayShootEvent structs if successful, or an empty array on failure.</returns>
        /// <exception cref="ArgumentNullException">geometry is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">maxReflections is strictly outside the [1-1000] range.</exception>
        public static RayShootEvent[] RayShoot(IEnumerable<GeometryBase> geometry, Ray3d ray, int maxReflections)
        {
            return ComputeServer.Post<RayShootEvent[]>(ApiAddress(), geometry, ray, maxReflections);
        }

        /// <summary>
        /// Computes point intersections that occur when shooting a ray to a collection of surfaces and Breps.
        /// </summary>
        /// <param name="geometry">The collection of surfaces and Breps to intersect. Trims are ignored on Breps.</param>
        /// <param name="ray">>A ray used in intersection.</param>
        /// <param name="maxReflections">The maximum number of reflections. This value should be any value between 1 and 1000, inclusive.</param>
        /// <returns>An array of RayShootEvent structs if successful, or an empty array on failure.</returns>
        /// <exception cref="ArgumentNullException">geometry is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">maxReflections is strictly outside the [1-1000] range.</exception>
        public static RayShootEvent[] RayShoot(Remote<IEnumerable<GeometryBase>> geometry, Ray3d ray, int maxReflections)
        {
            return ComputeServer.Post<RayShootEvent[]>(ApiAddress(), geometry, ray, maxReflections);
        }

        /// <summary>
                /// Projects points onto meshes.
                /// </summary>
                /// <param name="meshes">the meshes to project on to.</param>
                /// <param name="points">the points to project.</param>
                /// <param name="direction">the direction to project.</param>
                /// <param name="tolerance">
                /// Projection tolerances used for culling close points and for line-mesh intersection.
                /// </param>
                /// <returns>
                /// Array of projected points, or null in case of any error or invalid input.
                /// </returns>
        public static Point3d[] ProjectPointsToMeshes(IEnumerable<Mesh> meshes, IEnumerable<Point3d> points, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), meshes, points, direction, tolerance);
        }

        /// <summary>
                /// Projects points onto meshes.
                /// </summary>
                /// <param name="meshes">the meshes to project on to.</param>
                /// <param name="points">the points to project.</param>
                /// <param name="direction">the direction to project.</param>
                /// <param name="tolerance">
                /// Projection tolerances used for culling close points and for line-mesh intersection.
                /// </param>
                /// <returns>
                /// Array of projected points, or null in case of any error or invalid input.
                /// </returns>
        public static Point3d[] ProjectPointsToMeshes(Remote<IEnumerable<Mesh>> meshes, IEnumerable<Point3d> points, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), meshes, points, direction, tolerance);
        }

        /// <summary>
        /// Projects points onto meshes.
        /// </summary>
        /// <param name="meshes">the meshes to project on to.</param>
        /// <param name="points">the points to project.</param>
        /// <param name="direction">the direction to project.</param>
        /// <param name="tolerance">
        /// Projection tolerances used for culling close points and for line-mesh intersection.
        /// </param>
        /// <param name="indices">Return points[i] is a projection of points[indices[i]]</param>
        /// <returns>
        /// Array of projected points, or null in case of any error or invalid input.
        /// </returns>
        /// <example>
        /// <code source='examples\vbnet\ex_projectpointstomeshesex.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_projectpointstomeshesex.cs' lang='cs'/>
        /// <code source='examples\py\ex_projectpointstomeshesex.py' lang='py'/>
        /// </example>
        public static Point3d[] ProjectPointsToMeshesEx(IEnumerable<Mesh> meshes, IEnumerable<Point3d> points, Vector3d direction, double tolerance, out int[] indices)
        {
            return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out indices, meshes, points, direction, tolerance);
        }

        /// <summary>
        /// Projects points onto meshes.
        /// </summary>
        /// <param name="meshes">the meshes to project on to.</param>
        /// <param name="points">the points to project.</param>
        /// <param name="direction">the direction to project.</param>
        /// <param name="tolerance">
        /// Projection tolerances used for culling close points and for line-mesh intersection.
        /// </param>
        /// <param name="indices">Return points[i] is a projection of points[indices[i]]</param>
        /// <returns>
        /// Array of projected points, or null in case of any error or invalid input.
        /// </returns>
        /// <example>
        /// <code source='examples\vbnet\ex_projectpointstomeshesex.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_projectpointstomeshesex.cs' lang='cs'/>
        /// <code source='examples\py\ex_projectpointstomeshesex.py' lang='py'/>
        /// </example>
        public static Point3d[] ProjectPointsToMeshesEx(Remote<IEnumerable<Mesh>> meshes, IEnumerable<Point3d> points, Vector3d direction, double tolerance, out int[] indices)
        {
            return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out indices, meshes, points, direction, tolerance);
        }

        /// <summary>
        /// Projects points onto breps.
        /// </summary>
        /// <param name="breps">The breps projection targets.</param>
        /// <param name="points">The points to project.</param>
        /// <param name="direction">The direction to project.</param>
        /// <param name="tolerance">The tolerance used for intersections.</param>
        /// <returns>
        /// Array of projected points, or null in case of any error or invalid input.
        /// </returns>
        /// <example>
        /// <code source='examples\vbnet\ex_projectpointstobreps.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_projectpointstobreps.cs' lang='cs'/>
        /// <code source='examples\py\ex_projectpointstobreps.py' lang='py'/>
        /// </example>
        public static Point3d[] ProjectPointsToBreps(IEnumerable<Brep> breps, IEnumerable<Point3d> points, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), breps, points, direction, tolerance);
        }

        /// <summary>
        /// Projects points onto breps.
        /// </summary>
        /// <param name="breps">The breps projection targets.</param>
        /// <param name="points">The points to project.</param>
        /// <param name="direction">The direction to project.</param>
        /// <param name="tolerance">The tolerance used for intersections.</param>
        /// <returns>
        /// Array of projected points, or null in case of any error or invalid input.
        /// </returns>
        /// <example>
        /// <code source='examples\vbnet\ex_projectpointstobreps.vb' lang='vbnet'/>
        /// <code source='examples\cs\ex_projectpointstobreps.cs' lang='cs'/>
        /// <code source='examples\py\ex_projectpointstobreps.py' lang='py'/>
        /// </example>
        public static Point3d[] ProjectPointsToBreps(Remote<IEnumerable<Brep>> breps, IEnumerable<Point3d> points, Vector3d direction, double tolerance)
        {
            return ComputeServer.Post<Point3d[]>(ApiAddress(), breps, points, direction, tolerance);
        }

        /// <summary>
        /// Projects points onto breps.
        /// </summary>
        /// <param name="breps">The breps projection targets.</param>
        /// <param name="points">The points to project.</param>
        /// <param name="direction">The direction to project.</param>
        /// <param name="tolerance">The tolerance used for intersections.</param>
        /// <param name="indices">Return points[i] is a projection of points[indices[i]]</param>
        /// <returns>
        /// Array of projected points, or null in case of any error or invalid input.
        /// </returns>
        public static Point3d[] ProjectPointsToBrepsEx(IEnumerable<Brep> breps, IEnumerable<Point3d> points, Vector3d direction, double tolerance, out int[] indices)
        {
            return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out indices, breps, points, direction, tolerance);
        }

        /// <summary>
        /// Projects points onto breps.
        /// </summary>
        /// <param name="breps">The breps projection targets.</param>
        /// <param name="points">The points to project.</param>
        /// <param name="direction">The direction to project.</param>
        /// <param name="tolerance">The tolerance used for intersections.</param>
        /// <param name="indices">Return points[i] is a projection of points[indices[i]]</param>
        /// <returns>
        /// Array of projected points, or null in case of any error or invalid input.
        /// </returns>
        public static Point3d[] ProjectPointsToBrepsEx(Remote<IEnumerable<Brep>> breps, IEnumerable<Point3d> points, Vector3d direction, double tolerance, out int[] indices)
        {
            return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out indices, breps, points, direction, tolerance);
        }
    }
