/// <reference path="../../../typings/tsd.d.ts" />

applicationModule.factory("traceFactorySignalRService", ["$rootScope", "$log", "$q", function ($rootScope, $log, $q) {
    function traceOperations() {

        // callback methods
        var addNewMessage;
        var forceUpdate;
        var setCallbacks = function (addNewMessageCallback, forceUpdateCallback) {
            addNewMessage = addNewMessageCallback;
            forceUpdate = forceUpdateCallback;
        };

        // main objects
        var connectionHub;
        var connectionHubProxy;

        // cliend side methods
        var configureProxyClientFunctions = function () {
            connectionHubProxy.on("notify", function (data) {
                $rootScope.$apply(addNewMessage(data));
            });

            connectionHubProxy.on("notifyConsole", function (data) {
                $log.warn(data);
            });

            connectionHubProxy.on("forceUpdate", function (data) {
                $rootScope.$apply(forceUpdate(data));
            });
        };

        var getClients = function () {
            var deferred = $q.defer();
            connectionHubProxy.invoke("getClientNames")
                .done(function (data) {
                    deferred.resolve(data);
                    //x $rootScope.$$phase || $rootScope.$apply();
                })
                .fail(function (error) {
                    $log.error(error);
                    deferred.reject();
                });
            return deferred.promise;
        };

        var getGroups = function () {
            var deferred = $q.defer();
            connectionHubProxy.invoke("getGroupNames")
                .done(function (data) {
                    deferred.resolve(data);
                    //x $rootScope.$$phase || $rootScope.$apply();
                })
                .fail(function (error) {
                    $log.error(error);
                    deferred.reject();
                });
            return deferred.promise;
        };


        var getDebugValues = function () {
            var deferred = $q.defer();
            connectionHubProxy.invoke("getDebugValues")
                .done(function (data) {
                    deferred.resolve(data);
                    //x $rootScope.$$phase || $rootScope.$apply();
                })
                .fail(function (error) {
                    $log.error(error);
                    deferred.reject();
                });
            return deferred.promise;
        };

        var connect = function () {
            var isDisconnected = connectionHub && connectionHub.state === $.signalR.connectionState.disconnected;
            if (isDisconnected) {
                connectionHub.start()
                /*
                .pipe(function () {
                    //x $rootScope.$apply(setConnectionStatus(true));
                });
                */
                    .done(function () { $log.info("SignalR connected, connection ID=" + connectionHub.id); })
                    .fail(function () { $log.info("SignalR could not connect"); });
            }
        };

        var disconnect = function () {
            connectionHub.stop(true, true);
            //x $rootScope.$apply(setConnectionStatus(false));
        };

        var initializeClient = function () {
            connectionHub = $.hubConnection(); // = $.connection.hub
            connectionHub.qs = { "Instance": "Observer" };
            connectionHub.logging = false;
            connectionHubProxy = connectionHub.createHubProxy("traceHub");
            configureProxyClientFunctions();
        };

        var isConnected = function () {
            var output = connectionHub.state === $.signalR.connectionState.connected;
            return output;
        }

        return {
            initializeClient: initializeClient,
            setCallbacks: setCallbacks,
            getClients: getClients,
            getGroups: getGroups,
            getDebugValues: getDebugValues,
            connect: connect,
            disconnect: disconnect,
            isConnected: isConnected
        }
    };

    return traceOperations;
}]);
