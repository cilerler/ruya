//x / <binding AfterBuild='build:all' Clean='clean:all' />

// MS-DOS || POST-BUILD
//x set NODE_ENV = $(ConfigurationName)
// gulp
// POWERSHELL
//x $env:NODE_ENV = $(ConfigurationName)
var configuration = "Release";
var isRelease = true;
if (process.env.NODE_ENV && process.env.NODE_ENV !== configuration) {
    isRelease = false;
    configuration = process.env.NODE_ENV;
}
console.log("Configruation: " + configuration);

var pathSeparator = "/";

var fs = require("fs");
var rimraf = require("rimraf");
var gulp = require("gulp");
var gulpSourcemaps = require("gulp-sourcemaps");
var bower = require("./bower.json");

var projectInput = "." + pathSeparator + "WebContent" + pathSeparator;
var projectOutput = "." + pathSeparator + "bin/" + configuration + "/wwwroot" + pathSeparator;

gulp.task("default", ["clean:all", "build:all"], function () {
});

gulp.task("clean:all", ["clean:documents", "clean:imagesFolder", "clean:libFolder", "clean:cssFolder", "clean:scriptsFolder"], function () {
});

gulp.task("build:all", ["build:documents", "build:imagesFolder", "build:libFolder", "build:cssFolder", "build:scriptsFolder"], function () {
});

//#region LibFolder (Bower)
var bowerPaths = {
    source: "./bower_components/",
    target: projectOutput + "lib/"
};

gulp.task("clean:libFolder", function (input) {
    rimraf(bowerPaths.target, input);
});

gulp.task("build:libFolder", ["clean:libFolder"], function () {
    for (var bowerComponent in bower.exportsOverride) {
        for (var componentDirectories in bower.exportsOverride[bowerComponent]) {

            var destinationFolder = bowerPaths.target + bowerComponent + pathSeparator;
            if (componentDirectories !== "") destinationFolder += componentDirectories;

            var sourceFiles = bower.exportsOverride[bowerComponent][componentDirectories];
            var files = Array.isArray(sourceFiles) ? sourceFiles : [sourceFiles];
            var sourceFolderPrefix = bowerPaths.source + bowerComponent + pathSeparator;
            for (var file in files) {
                var sourceFolder = sourceFolderPrefix + files[file];

                gulp.src(sourceFolder).pipe(gulp.dest(destinationFolder));

            }
        }
    }
});
//#endregion

//#region ScriptsFolder (TypeScript)
var typescriptPaths = {
    source: projectInput + "scripts/**/*.ts",
    target: projectOutput + "scripts/"
};

var merge = require("merge2");
var gulpTypescript = require("gulp-typescript");
var gulpConcat = require("gulp-concat");
var gulpJshint = require("gulp-jshint");
var gulpUglify = require("gulp-uglify");
var gulpRename = require("gulp-rename");

gulp.task("clean:scriptsFolder", function (input) {
    rimraf(typescriptPaths.target, input);
});

gulp.task("build:scriptsFolder", ["clean:scriptsFolder"], function () {
    var typescriptResult = gulp.src(typescriptPaths.source)
        .pipe(gulpSourcemaps.init(
            { loadMaps: true }
        ))
        .pipe(gulpTypescript({
            sortOutput: true,
            declarationFiles: true,
            noExternalResolve: false,
            target: "ES5"
        }));

    return merge([
        typescriptResult.js
        .pipe(gulpConcat("application.js"))
        .pipe(gulpJshint())
        .pipe(gulpUglify({
            outSourceMap: true,
            sourceRoot: typescriptPaths.target
        }))
        .pipe(gulpRename({
            extname: ".min.js"
        }))
        .pipe(gulpSourcemaps.write(".",
            { sourceRoot: typescriptPaths.target }
        ))
        .pipe(gulp.dest(typescriptPaths.target)),
        typescriptResult.dts
        .pipe(gulpConcat("application.d.ts"))
        .pipe(gulp.dest(typescriptPaths.target))
    ]);
});

gulp.task("watch_scriptsFolder", ["build:scriptsFolder"], function () {
    gulp.watch(typescriptPaths.source + "**/*.ts", ["build:scriptsFolder"]);
});
//#endregion

//#region CssFolder (StyleSheets)
var gulpMinifyCss = require("gulp-minify-css");

var sassPaths = {
    source: projectInput + "StyleSheets/**/*.scss",
    target: projectOutput + "css/"
};

var gulpSass = require("gulp-sass");

gulp.task("clean:cssFolder", function (input) {
    rimraf(projectOutput + "css/", input);
});

gulp.task("build:cssFolder", ["clean:cssFolder"], function () {
    gulp.src(sassPaths.source)
        .pipe(gulpSourcemaps.init())
        .pipe(gulpSass())
        .pipe(gulpMinifyCss())
        .pipe(gulpSourcemaps.write(".", { sourceRoot: sassPaths.target }))
        .pipe(gulp.dest(sassPaths.target));
});
//#endregion

//#region ImagesFolder
var imagesPaths = {
    source: projectInput + "images/**/*.*",
    target: projectOutput + "images/"
};

gulp.task("clean:imagesFolder", function (input) {
    rimraf(imagesPaths.target + "images/", input);
});

gulp.task("build:imagesFolder", ["clean:imagesFolder"], function () {
    return gulp.src(imagesPaths.source)
        .pipe(gulp.dest(imagesPaths.target));
});

//#endregion

//#region Documents (html)
var documentPaths = {
    source: projectInput + "**/*.{html,xml,xslt}",
    target: projectOutput
};

gulp.task("clean:documents", function (input) {
    rimraf(documentPaths.target, input);
});

gulp.task("build:documents", ["clean:documents"], function () {
    return gulp.src(documentPaths.source)
        .pipe(gulp.dest(documentPaths.target));
});

//#endregion