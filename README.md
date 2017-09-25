# What is this?

Hacky Xamarin.Android Java class browser.

Unlike other ordinal Java class browsers, you can pick either of:

	- Jar
	- Aar
	- Java Binding DLL
	- API description XML (either from class-parse output or api-xml-adjuster)

So far you'll have to specify these environment variables to run this:

- ANDROID_SDK_PATH: path to your Android SDK. Typically `~/android-sdk-(os)`.
- MONO_ANDROID_PATH: path to your Xamarin.Android setup, namely parent of
  `lib/xamarin.android/xbuild-frameworks/MonoAndroid`.
  - if you are using xamarin-android OSS, it will be `bin/Debug`.
