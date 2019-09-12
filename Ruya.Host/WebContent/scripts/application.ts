/// <reference path="../../typings/tsd.d.ts" />

var applicationModule = angular.module("app", ["ui.bootstrap"]);

//#region //!++ IIFE (Invoked Function Expressions)
((): void => {
    "use strict";

    //angular.module("app", []);

})();
//#endregion

// stops the hub if the user leaves the page
window.onbeforeunload = function (e) {
    $.connection.hub.stop();
};