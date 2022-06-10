// by Freya Holmér (https://github.com/FreyaHolmer/Mathfs)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Freya {

	public static class MathfsCodegen {

		class SplineType {
			public int degree;
			public string className;
			public string prettyName;
			public string prettyNameLower;
			public string[] paramNames;
			public string[] paramDescs;
			public string matrixName;
			public RationalMatrix4x4 charMatrix;

			public SplineType( int degree, string className, string prettyName, string matrixName, RationalMatrix4x4 charMatrix, string[] paramNames, string[] paramDescs, string[] paramDescsQuad = null ) {
				this.degree = degree;
				this.className = className;
				this.prettyName = prettyName;
				this.prettyNameLower = prettyName.ToLowerInvariant();
				this.paramDescs = paramDescs;
				this.matrixName = matrixName;
				this.paramNames = paramNames;
				this.charMatrix = charMatrix;
			}

			public void AppendParamStrings( CodeGenerator gen, int degree, int i ) {
				gen.Param( paramNames[i], paramDescs[i] );
			}
		}

		#region Type Definitions

		static SplineType typeBezier = new SplineType( 3, "Bezier", "Bézier", "cubicBezier", CharMatrix.cubicBezier,
			new[] { "p0", "p1", "p2", "p3" },
			new[] {
				"The starting point of the curve",
				"The second control point of the curve, sometimes called the start tangent point",
				"The third control point of the curve, sometimes called the end tangent point",
				"The end point of the curve"
			}
		);

		static SplineType typeBezierQuad = new SplineType( 2, "Bezier", "Bézier", "quadraticBezier", (RationalMatrix4x4)CharMatrix.quadraticBezier,
			new[] { "p0", "p1", "p2" },
			new[] {
				"The starting point of the curve",
				"The middle control point of the curve, sometimes called a tangent point",
				"The end point of the curve"
			}
		);

		static SplineType typeHermite = new SplineType( 3, "Hermite", "Hermite", "cubicHermite", CharMatrix.cubicHermite,
			new[] { "p0", "v0", "p1", "v1" },
			new[] {
				"The starting point of the curve",
				"The rate of change (velocity) at the start of the curve",
				"The end point of the curve",
				"The rate of change (velocity) at the end of the curve"
			}
		);

		static SplineType typeBspline = new SplineType( 3, "UBS", "B-Spline", "cubicUniformBspline", CharMatrix.cubicUniformBspline,
			new[] { "p0", "p1", "p2", "p3" },
			new[] {
				"The first point of the B-spline hull",
				"The second point of the B-spline hull",
				"The third point of the B-spline hull",
				"The fourth point of the B-spline hull"
			}
		);

		static SplineType typeCatRom = new SplineType( 3, "CatRom", "Catmull-Rom", "cubicCatmullRom", CharMatrix.cubicCatmullRom,
			new[] { "p0", "p1", "p2", "p3" },
			new[] {
				"The first control point of the catmull-rom curve. Note that this point is not included in the curve itself, and only helps to shape it",
				"The second control point, and the start of the catmull-rom curve",
				"The third control point, and the end of the catmull-rom curve",
				"The last control point of the catmull-rom curve. Note that this point is not included in the curve itself, and only helps to shape it"
			}
		);

		#endregion

		[MenuItem( "Assets/Run Mathfs Codegen" )]
		public static void Regenerate() {
			for( int dim = 1; dim < 4; dim++ ) { // 1D, 2D, 3D
				GenerateType( typeBezier, dim );
				GenerateType( typeBezierQuad, dim );
				GenerateType( typeHermite, dim );
				GenerateType( typeBspline, dim );
				GenerateType( typeCatRom, dim );
			}
		}

		public static string GetLerpName( int dim ) {
			return dim switch {
				1 => "Mathfs.Lerp",
				2 => "Vector2.LerpUnclamped",
				3 => "Vector3.LerpUnclamped",
				4 => "Vector4.LerpUnclamped",
				_ => throw new IndexOutOfRangeException()
			};
		}

		static void GenerateType( SplineType type, int dim ) {
			int degree = type.degree;
			string dataType = dim == 1 ? "float" : $"Vector{dim}";
			string polynomType = dim == 1 ? "Polynomial" : $"Polynomial{dim}D";
			int ptCount = degree + 1;
			string degFullLower = GetDegreeName( degree, false );
			string degShortCapital = GetDegreeName( degree, true );
			string structName = $"{type.className}{degShortCapital}{dim}D";
			string[] points = type.paramNames;
			string[] pointDescs = type.paramDescs;
			string lerpName = GetLerpName( dim );
			string pointMatrixType = $"{( dim == 1 ? "" : dataType )}Matrix{ptCount}x1";

			CodeGenerator code = new CodeGenerator();
			code.Comment( "by Freya Holmér (https://github.com/FreyaHolmer/Mathfs)" );
			code.Comment( $"Do not manually edit - this file is generated by {nameof(MathfsCodegen)}.cs" );
			code.LineBreak();
			code.Using( "System" );
			code.Using( "System.Runtime.CompilerServices" );
			code.Using( "UnityEngine" );
			code.LineBreak();

			using( code.BracketScope( "namespace Freya" ) ) {
				code.LineBreak();

				// type definition
				code.Summary( $"An optimized uniform {dim}D {degFullLower} {type.prettyNameLower} segment, with {ptCount} control points" );
				using( code.BracketScope( $"[Serializable] public struct {structName} : IParamCubicSplineSegment{dim}D" ) ) { // intentionally always Cubic right now
					code.LineBreak();
					code.Append( "const MethodImplOptions INLINE = MethodImplOptions.AggressiveInlining;" );
					code.LineBreak();

					// constructor
					code.Summary( $"Creates a uniform {dim}D {degFullLower} {type.prettyNameLower} segment, from {ptCount} control points" );
					for( int i = 0; i < ptCount; i++ )
						type.AppendParamStrings( code, degree, i );
					using( code.BracketScope( $"public {structName}( {string.Join( ", ", points.Select( p => $"{dataType} {p}" ) )} )" ) ) {
						code.Append( $"( {string.Join( ", ", points.Select( p => $"this.{p}" ) )} ) = ( {string.Join( ", ", points )} );" );
						code.Append( "validCoefficients = false;" );
						code.Append( "curve = default;" );
					}

					code.LineBreak();

					// Curve
					code.Append( $"{polynomType} curve;" );
					using( code.BracketScope( $"public {polynomType} Curve" ) ) {
						using( code.BracketScope( $"get" ) ) {
							code.Append( "ReadyCoefficients();" );
							code.Append( "return curve;" );
						}
					}

					// control point properties
					using( code.ScopeRegion( "Control Points" ) ) {
						code.Append( $"[SerializeField] {dataType} {string.Join( ", ", points )};" );
						code.Append( $"public {pointMatrixType} PointMatrix => new({string.Join( ", ", points )});" );
						code.LineBreak();
						for( int i = 0; i < ptCount; i++ ) {
							code.Summary( pointDescs[i] );
							using( code.BracketScope( $"public {dataType} {points[i].ToUpperInvariant()}" ) ) {
								code.Append( $"[MethodImpl( INLINE )] get => {points[i]};" );
								code.Append( $"[MethodImpl( INLINE )] set => _ = ( {points[i]} = value, validCoefficients = false );" );
							}

							code.LineBreak();
						}

						code.Summary( $"Get or set a control point position by index. Valid indices from 0 to {degree}" );
						using( code.BracketScope( $"public {dataType} this[ int i ]" ) ) {
							using( code.Scope( "get =>" ) ) {
								using( code.Scope( "i switch {" ) ) {
									for( int i = 0; i < ptCount; i++ )
										code.Append( $"{i} => {points[i].ToUpperInvariant()}," );
									code.Append( $"_ => throw new ArgumentOutOfRangeException( nameof(i), $\"Index has to be in the 0 to {degree} range, and I think {{i}} is outside that range you know\" )" );
								}

								code.Append( "};" );
							}

							using( code.BracketScope( "set" ) ) {
								using( code.BracketScope( "switch( i )" ) ) {
									for( int i = 0; i < ptCount; i++ ) {
										using( code.Scope( $"case {i}:" ) ) {
											code.Append( $"{points[i].ToUpperInvariant()} = value;" );
											code.Append( "break;" );
										}
									}

									code.Append( $"default: throw new ArgumentOutOfRangeException( nameof(i), $\"Index has to be in the 0 to {degree} range, and I think {{i}} is outside that range you know\" );" );
								}
							}
						}
					}

					// Coefficients
					code.Append( "[NonSerialized] bool validCoefficients;" );
					code.LineBreak();
					using( code.BracketScope( "[MethodImpl( INLINE )] void ReadyCoefficients()" ) ) {
						using( code.Scope( "if( validCoefficients )" ) )
							code.Append( "return; // no need to update" );
						code.Append( "validCoefficients = true;" );


						using( code.Scope( $"curve = new {polynomType}(" ) ) {
							for( int icRow = 0; icRow < ptCount; icRow++ ) {
								MathSum sum = new MathSum();
								for( int ip = 0; ip < ptCount; ip++ )
									sum.AddTerm( type.charMatrix[icRow, ip], $"{type.paramNames[ip]}" );
								code.Append( $"{sum}{( icRow < ptCount - 1 ? "," : "" )}" );
							}
						}

						code.Append( ");" );

						// todo: unroll matrix multiply for performance
						// code.Append( $"curve = new {polynomType}( CharMatrix.{type.matrixName} * PointMatrix );" );
					}

					// equality checks
					code.Append( $"public static bool operator ==( {structName} a, {structName} b ) => {string.Join( " && ", points.Select( p => $"a.{p.ToUpperInvariant()} == b.{p.ToUpperInvariant()}" ) )};" );
					code.Append( $"public static bool operator !=( {structName} a, {structName} b ) => !( a == b );" );
					code.Append( $"public bool Equals( {structName} other ) => {string.Join( " && ", points.Select( p => $"{p.ToUpperInvariant()}.Equals( other.{p.ToUpperInvariant()} )" ) )};" );
					code.Append( $"public override bool Equals( object obj ) => obj is {structName} other && Equals( other );" );
					code.Append( $"public override int GetHashCode() => HashCode.Combine( {string.Join( ", ", points )} );" );
					code.LineBreak();
					code.Append( $"public override string ToString() => $\"({string.Join( ", ", points.Select( p => $"{{{p}}}" ) )})\";" );

					// typecasting
					if( dim is 2 or 3 && degree is 3 ) {
						if( dim == 2 ) {
							// Typecast to 3D where z = 0
							string structName3D = $"{type.className}{degShortCapital}3D";
							code.Summary( "Returns this spline segment in 3D, where z = 0" );
							code.Param( "curve2D", "The 2D curve to cast to 3D" );
							code.Append( $"public static explicit operator {structName3D}( {structName} curve2D ) => new {structName3D}( {string.Join( ", ", points.Select( p => $"curve2D.{p}" ) )} );" );
						}

						if( dim == 3 ) {
							// typecast to 2D where z is omitted
							string structName2D = $"{type.className}{degShortCapital}2D";
							code.Summary( "Returns this curve flattened to 2D. Effectively setting z = 0" );
							code.Param( "curve3D", "The 3D curve to flatten to the Z plane" );
							code.Append( $"public static explicit operator {structName2D}( {structName} curve3D ) => new {structName2D}( {string.Join( ", ", points.Select( p => $"curve3D.{p}" ) )} );" );
						}
					}

					// converting between spline types
					if( degree == 3 ) {
						string[] cubicSplineTypeNames = {
							nameof(BezierCubic1D).Replace( "1D", $"{dim}D" ),
							nameof(HermiteCubic1D).Replace( "1D", $"{dim}D" ),
							nameof(CatRomCubic1D).Replace( "1D", $"{dim}D" ),
							nameof(UBSCubic1D).Replace( "1D", $"{dim}D" )
						};
						RationalMatrix4x4[] typeMatrices = {
							CharMatrix.cubicBezier,
							CharMatrix.cubicHermite,
							CharMatrix.cubicCatmullRom,
							CharMatrix.cubicUniformBspline
						};

						// Conversion to other cubic splines
						for( int i = 0; i < 4; i++ ) {
							string targetType = cubicSplineTypeNames[i];
							if( targetType == structName )
								continue; // don't convert to self
							RationalMatrix4x4 C = CharMatrix.GetConversionMatrix( type.charMatrix, typeMatrices[i] );

							using( code.Scope( $"public static explicit operator {targetType}( {structName} s ) =>" ) ) {
								using( code.Scope( $"new {targetType}(" ) ) {
									for( int oPt = 0; oPt < 4; oPt++ ) {
										MathSum sum = new();
										for( int iPt = 0; iPt < 4; iPt++ )
											sum.AddTerm( C[oPt, iPt], $"s.{type.paramNames[iPt]}" );
										code.Append( $"{sum}{( oPt < 3 ? "," : "" )}" );
									}
								}

								code.Append( ");" );
							}
						}
					}

					// Interpolation
					code.Summary( $"Returns a linear blend between two {type.prettyNameLower} curves" );
					code.Param( "a", "The first spline segment" );
					code.Param( "b", "The second spline segment" );
					code.Param( "t", "A value from 0 to 1 to blend between <c>a</c> and <c>b</c>" );
					using( code.Scope( $"public static {structName} Lerp( {structName} a, {structName} b, float t ) =>" ) ) {
						using( code.Scope( "new(" ) ) {
							for( int i = 0; i < ptCount; i++ ) {
								code.Append( $"{lerpName}( a.{points[i]}, b.{points[i]}, t )" + ( i == ptCount - 1 ? "" : "," ) );
							}
						}

						code.Append( ");" );
					}


					// special case slerps for cubic beziers in 2D and 3D
					if( dim > 1 && degree is 2 or 3 && type == typeBezier ) {
						// todo: hermite slerp
						string slerpCast = dim == 2 ? "(Vector2)" : "";
						code.LineBreak();
						code.Summary( $"Returns a linear blend between two {type.prettyNameLower} curves, where the tangent directions are spherically interpolated" );
						code.Param( "a", "The first spline segment" );
						code.Param( "b", "The second spline segment" );
						code.Param( "t", "A value from 0 to 1 to blend between <c>a</c> and <c>b</c>" );
						using( code.BracketScope( $"public static {structName} Slerp( {structName} a, {structName} b, float t )" ) ) {
							code.Append( $"{dataType} p0 = {lerpName}( a.p0, b.p0, t );" );
							code.Append( $"{dataType} p3 = {lerpName}( a.p3, b.p3, t );" );
							using( code.Scope( $"return new {structName}(" ) ) {
								code.Append( $"p0," );
								code.Append( $"p0 + {slerpCast}Vector3.SlerpUnclamped( a.p1 - a.p0, b.p1 - b.p0, t )," );
								code.Append( $"p3 + {slerpCast}Vector3.SlerpUnclamped( a.p2 - a.p3, b.p2 - b.p3, t )," );
								code.Append( $"p3" );
							}

							code.Append( ");" );
						}
					}

					// special case splits
					bool hasSplit = type == typeBezier || type == typeBezierQuad;
					if( hasSplit ) {
						code.Summary( "Splits this curve at the given t-value, into two curves that together form the exact same shape" );
						code.Param( "t", "The t-value to split at" );
						using( code.BracketScope( $"public ({structName} pre, {structName} post) Split( float t )" ) ) {
							AppendBezierSplit( code, structName, dataType, degree, dim );
						}
					}
				}
			}

			string path = $"Assets/Mathfs/Splines/Uniform Spline Segments/{structName}.cs";
			File.WriteAllLines( path, code.content );
		}

		class MathSum {

			Rational globalScale = Rational.One;
			List<(Rational coeff, string var)> terms = new List<(Rational coeff, string var)>();

			public void AddTerm( Rational coeff, string var ) {
				if( coeff != 0 )
					terms.Add( ( coeff, var ) );
			}

			void TryOptimize() {
				if( terms.Count < 2 )
					return; // can't optimize 0 or 1 terms

				Rational coeff0 = terms[0].coeff.Abs();
				if( terms.TrueForAll( t => t.coeff.Abs() == coeff0 ) ) {
					globalScale = coeff0;
					for( int i = 0; i < terms.Count; i++ )
						terms[i] = ( terms[i].coeff / coeff0, terms[i].var );
				}
			}

			public override string ToString() {
				if( terms.Count == 0 )
					return "0";

				TryOptimize();

				string line = "";
				for( int i = 0; i < terms.Count; i++ )
					line += FormatTerm( i );

				if( globalScale != 1 ) {

					if( globalScale.n == 1 ) {
						line = $"({line})/{globalScale.d}";
					} else {
						line = $"{FormatRational( globalScale )}*({line})";
					}
					
				}

				return line;
			}

			string FormatRational( Rational v ) => v.IsInteger ? $"{v.n}" : $"({v}f)";

			string FormatTerm( int i ) {
				Rational value = terms[i].coeff;
				string sign = i > 0 && value >= 0 ? "+" : "";
				string valueStr;
				string op = "";
				if( value == Rational.One )
					valueStr = "";
				else if( value == -Rational.One )
					valueStr = "-";
				else if( value > 0 ) {
					valueStr = FormatRational( value );
					op = "*";
				} else { // value < 0
					valueStr = FormatRational( -value );
					sign = "-";
					op = "*";
				}

				return $"{sign}{valueStr}{op}{terms[i].var}";
			}

		}

		public static string GetDegreeName( int d, bool shortName ) {
			return d switch {
				1 => "Linear",
				2 => shortName ? "Quad" : "Quadratic",
				3 => "Cubic",
				4 => "Quartic",
				5 => "Quintic",
				_ => throw new IndexOutOfRangeException()
			};
		}

		static readonly string[] comp = { "x", "y", "z" };

		public static void AppendBezierSplit( CodeGenerator code, string structName, string dataType, int degree, int dim ) {
			string LerpStr( string A, string B, int c ) => $"{A}.{comp[c]} + ( {B}.{comp[c]} - {A}.{comp[c]} ) * t";

			void AppendLerps( string varName, string A, string B ) {
				if( dim > 1 ) {
					using( code.Scope( $"{dataType} {varName} = new {dataType}(" ) ) {
						for( int c = 0; c < dim; c++ ) {
							string end = c == dim - 1 ? " );" : ",";
							code.Append( $"{LerpStr( A, B, c )}{end}" );
						}
					}
				} else { // floats
					code.Append( $"{dataType} {varName} = {A} + ( {B} - {A} ) * t;" );
				}
			}

			AppendLerps( "a", "p0", "p1" );
			AppendLerps( "b", "p1", "p2" ); // this could be unrolled/optimized for the cubic case, as b is never used for the output
			if( degree == 3 ) {
				AppendLerps( "c", "p2", "p3" );
				AppendLerps( "d", "a", "b" );
				AppendLerps( "e", "b", "c" );
				AppendLerps( "p", "d", "e" );
				code.Append( $"return ( new {structName}( p0, a, d, p ), new {structName}( p, e, c, p3 ) );" );
			} else if( degree == 2 ) {
				AppendLerps( "p", "a", "b" );
				code.Append( $"return ( new {structName}( p0, a, p ), new {structName}( p, b, p2 ) );" );
			}
		}

	}

}