/// <reference path="../../../typings/tsd.d.ts" />

applicationModule.controller("traceController",
    ["$log", "$modal", "$scope", "traceFactorySignalRService", function ($log, $modal, $scope, traceFactorySignalRService) {
       
        //#region signalR

        $scope.updateClientsList = function () {
            $scope.signalRService.getClients().then(function (data) {
                $log.info(data);
            });
        }

        $scope.updateGroupsList = function () {
            $scope.signalRService.getGroups().then(function (data) {
                $log.info(data);
            });
        }

        $scope.updateDebugValuesList = function () {
            $scope.signalRService.getDebugValues().then(function (data) {
                $log.info(data);
            });
        }

        $scope.addNewMessage = function (message) {
            var config = {
                delimiter: ",", // auto-detect
                newline: "\r\n", // auto-detect
                header: true,
                dynamicTyping: false,
                preview: 0,
                encoding: "",
                worker: false,
                comments: false,
                step: undefined,
                complete: undefined,
                error: undefined,
                download: false,
                skipEmptyLines: false,
                chunk: undefined,
                fastMode: undefined,
                beforeFirstChunk: undefined
            };
            var header = "Source,EventType,EventId,Content,Unknown,ProcessId,LogicalOperationStack,ThreadId,DateTime,TimeStamp,CallStack";
            var input = header + "\r\n" + message;
            var results = Papa.parse(input, config);
            // add error handling
            $scope.messagesList.push(results.data[0]);
        }

        $scope.forceUpdate = function () {
            
        }

        $scope.signalRService = traceFactorySignalRService();
        $scope.signalRService.setCallbacks($scope.addNewMessage, $scope.forceUpdate);
        $scope.signalRService.initializeClient();

        //#endregion
       
        //#region Main
        var today = new Date();
        $scope.year = today.getFullYear();

        $scope.applicationBlocked = false;
        $scope.messagesList = [];
        $scope.clientsList = [];
        $scope.groupsList = [];

        $scope.forceUpdate();
        //#endregion
    }]);