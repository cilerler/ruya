<?xml version="1.0" encoding="utf-8"?>

<!--
USAGE:
  add line below next to <?xml version="1.0" encoding="utf-8"?> line in XML file
  <?xml-stylesheet type="text/xsl" href="TraceViewer.xslt" ?>
-->

<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:msxsl="urn:schemas-microsoft-com:xslt"
                xmlns:te="http://schemas.microsoft.com/2004/06/E2ETraceEvent"
                xmlns:s="http://schemas.microsoft.com/2004/06/windows/eventlog/system"
                xmlns:sd="http://schemas.microsoft.com/2004/08/System.Diagnostics"
                xmlns:tr="http://schemas.microsoft.com/2004/10/E2ETraceEvent/TraceRecord"
                exclude-result-prefixes="msxsl te s sd tr">

  <xsl:output method="html" indent="yes" />
  <xsl:key name="start" match="te:E2ETraceEvent[s:System/s:SubType/@Name='Start']"
           use="s:System/s:Correlation/@ActivityID" />

  <xsl:template match="/">
    <html>
      <head>
        <meta http-equiv="refresh" content="60" />
        <style>
          body {
          font-family: 'Helvetica Neue', Helvetica, Arial, sans-serif;
          }

          table {
          font-size: x-small;
          }

          caption {
          text-align:left;
          padding-left : 10px;
          }

          thead > tr {
          background-color: #e80c7a;
          color: white;
          }

          th {
          font-weight: bolder;
          }

          .danger {
          background-color: #f2dede;
          }

          .warning {
          background-color: #fcf8e3;
          }

          .active {
          background-color: #f5f5f5;
          }

          .success {
          background-color: #dff0d8;
          }

          .info {
          background-color: #d9edf7;
          }

          .default {
          background-color: #ffffff;
          }

          .time {
          font-weight: bolder;
          padding-left: 10px;
          }
        </style>
        <script type="text/javascript">
          function bodyOnloadHandler()
          {
          document.getElementById('clientTime').innerText = (new Date()).toUTCString();
          }
        </script>
      </head>
      <body onload="bodyOnloadHandler()">
        <div>
          <div>
            <table>
              <caption>
                [
                <span class="default">Start</span> |
                <span class="default">Transfer</span> |
                <span class="default">Suspend</span> |
                <span class="default">Resume</span> |
                <span class="default">Stop</span> |
                <span class="active">Verbose</span> |
                <span class="info">Information</span> |
                <span class="success">Warning</span> |
                <span class="warning">Error</span> |
                <span class="danger">Critical</span>
                ]
                <a href="https://msdn.microsoft.com/en-us/library/ms733025(v=vs.110).aspx" target="_blank">Trace Levels</a>
                &amp;
                <a
                  href="https://msdn.microsoft.com/en-us/library/system.diagnostics.xmlwritertracelistener(v=vs.110).aspx"
                  target="_blank">
                  Elements and attributes of the XML output
                </a>
                <span class="time" id="clientTime"></span>
              </caption>
              <thead>
                <tr>
                  <th>Position</th>
                  <th>Data Items</th>
                  <th>Logical Operations</th>
                  <th>Description</th>
                  <th>Level</th>
                  <th>Type</th>
                  <th>Time</th>
                  <th>Event ID</th>
                  <th>Source</th>
                  <th>Trace Identifier</th>
                  <th>Activity Name</th>
                  <th>Activity ID</th>
                  <th>Related Activity ID</th>
                  <th>Thread ID</th>
                  <th>Process ID</th>
                  <th>Process Name</th>
                  <th>Call Stack</th>
                </tr>
              </thead>
              <tbody>
                <xsl:for-each select="//te:E2ETraceEvent">
                  <xsl:sort select="position()" data-type="number" order="descending"/>

                  <xsl:variable name="level">
                    <xsl:value-of select=".//s:SubType/@Name" />
                  </xsl:variable>

                  <xsl:variable name="traceIdentifier">
                    <xsl:value-of select=".//te:ApplicationData//tr:TraceIdentifier/text()" />
                  </xsl:variable>

                  <xsl:variable name="relatedActivityID">
                    <xsl:value-of select=".//s:Correlation/@RelatedActivityID" />
                  </xsl:variable>

                  <xsl:variable name="callStack">
                    <xsl:value-of select=".//sd:Callstack/text()" />
                  </xsl:variable>

                  <tr>
                    <xsl:attribute name="class">
                      <xsl:choose>
                        <xsl:when test="$level = 'Start'">default</xsl:when>
                        <xsl:when test="$level = 'Transfer'">default</xsl:when>
                        <xsl:when test="$level = 'Suspend'">default</xsl:when>
                        <xsl:when test="$level = 'Resume'">default</xsl:when>
                        <xsl:when test="$level = 'Stop'">default</xsl:when>
                        <xsl:when test="$level = 'Verbose'">active</xsl:when>
                        <xsl:when test="$level = 'Information'">info</xsl:when>
                        <xsl:when test="$level = 'Warning'">success</xsl:when>
                        <xsl:when test="$level = 'Error'">warning</xsl:when>
                        <xsl:when test="$level = 'Critical'">danger</xsl:when>
                        <xsl:otherwise>default</xsl:otherwise>
                      </xsl:choose>
                    </xsl:attribute>

                    <!-- POSITION -->
                    <td>
                      <xsl:value-of select="position()" />
                    </td>

                    <!-- DATA ITEMS -->
                    <td>
                      <table>
                        <xsl:for-each select=".//te:DataItem">
                          <tr>
                            <td>
                              <xsl:value-of select="." />
                            </td>
                          </tr>
                        </xsl:for-each>
                      </table>
                    </td>

                    <!-- LOGICAL OPERATIONS -->
                    <td>
                      <xsl:for-each select=".//sd:LogicalOperation">
                        <xsl:value-of select="." />
                        <!-- be careful about separator space-->
                        <xsl:if test="position() != last()">
                          <br />
                        </xsl:if>
                      </xsl:for-each>
                    </td>

                    <!-- APPLICATION DATA (aka DESCRIPTION) -->
                    <td>
                      <xsl:value-of select=".//te:ApplicationData/text()" />
                    </td>

                    <!-- LEVEL -->
                    <td>
                      <xsl:value-of select="$level" />
                    </td>

                    <!-- TYPE -->
                    <td>
                      <xsl:if test=".//te:TraceData">Data</xsl:if>
                      <xsl:if test="not(.//te:TraceData)">Event</xsl:if>
                    </td>

                    <!-- TIME -->
                    <td>
                      <xsl:value-of select=".//s:TimeCreated/@SystemTime" />
                    </td>

                    <!-- EVENT ID -->
                    <td>
                      <xsl:value-of select=".//s:System//s:EventID" />
                    </td>

                    <!-- SOURCE NAME -->
                    <td>
                      <xsl:value-of select=".//s:Source/@Name" />
                    </td>

                    <!-- TRACE IDENTIFIER -->
                    <td>
                      <xsl:choose>
                        <xsl:when test="$traceIdentifier != ''">
                          <xsl:value-of select="$traceIdentifier" />
                        </xsl:when>
                        <xsl:otherwise>
                          <xsl:if test="$relatedActivityID != ''">Trace Transfer</xsl:if>
                        </xsl:otherwise>
                      </xsl:choose>
                    </td>

                    <!-- ACTIVITY NAME -->
                    <td>
                      <xsl:value-of select="key('start', s:System/s:Correlation/@ActivityID)/te:ApplicationData/text()" />
                    </td>

                    <!-- ACTIVITY ID -->
                    <td>
                      <xsl:value-of select=".//s:Correlation/@ActivityID" />
                    </td>

                    <!-- RELATED ACTIVITY ID -->
                    <td>
                      <xsl:value-of select="$relatedActivityID" />
                    </td>

                    <!-- THREAD ID -->
                    <td>
                      <xsl:value-of select=".//s:Execution/@ThreadID" />
                    </td>

                    <!-- PROCESS ID -->
                    <td>
                      <xsl:value-of select=".//s:Execution/@ProcessID" />
                    </td>

                    <!-- PROCESS NAME -->
                    <td>
                      <xsl:value-of select=".//s:Execution/@ProcessName" />
                    </td>

                    <!-- CALLSTACK -->
                    <td>
                      <xsl:if test="$callStack != ''">
                        <xsl:attribute name="title">
                          <xsl:call-template name="replace">
                            <xsl:with-param name="word" select="$callStack" />
                          </xsl:call-template>
                        </xsl:attribute>
                        <xsl:text>[+]</xsl:text>
                      </xsl:if>
                    </td>
                  </tr>
                </xsl:for-each>
              </tbody>
            </table>
          </div>
        </div>
      </body>
    </html>
  </xsl:template>

  <xsl:template name="replace">
    <xsl:param name="word" />
    <!-- carriage returns (#xD) and line feeds (#xA) -->
    <xsl:variable name="cr">
      <xsl:text>&#xD;&#xA;   </xsl:text>
    </xsl:variable>
    <xsl:choose>
      <xsl:when test="contains($word,$cr)">
        <xsl:value-of select="substring-before($word,$cr)" />
        <xsl:text>&#xD;&#xA;</xsl:text>
        <xsl:call-template name="replace">
          <xsl:with-param name="word" select="substring-after($word,$cr)" />
        </xsl:call-template>
      </xsl:when>
      <xsl:otherwise>
        <xsl:value-of select="$word" />
      </xsl:otherwise>
    </xsl:choose>
  </xsl:template>

</xsl:stylesheet>