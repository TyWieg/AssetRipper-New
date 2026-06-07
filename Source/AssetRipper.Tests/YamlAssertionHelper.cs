using NUnit.Framework;
using System;

namespace AssetRipper.Tests;

public static class YamlAssertionHelper
{
	/// <summary>
	/// Normalizes expected and actual YAML output to Unix-style (LF) line endings 
	/// and strips outer whitespace to prevent cross-platform carriage return mismatch.
	/// </summary>
	/// <param name="expected">The expected ground-truth YAML string.</param>
	/// <param name="actual">The generated YAML string output under test.</param>
	public static void AssertYamlAreEqual(string? expected, string? actual)
	{
		string normalizedExpected = NormalizeLineEndings(expected);
		string normalizedActual = NormalizeLineEndings(actual);

		Assert.That(normalizedActual, Is.EqualTo(normalizedExpected));
	}

	private static string NormalizeLineEndings(string? value)
	{
		if (value is null)
		{
			return string.Empty;
		}

		return value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
	}
}