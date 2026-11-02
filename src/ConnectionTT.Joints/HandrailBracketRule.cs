using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.CADLink.Database;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Geometry;
using Autodesk.AdvanceSteel.Modelling;
using Autodesk.AdvanceSteel.Profiles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ConnectionTT.Joints;

// v4 - Etapa 3: duas chapas (sem parafusos/soldas).
//  v10: largura da chapa de ancoragem direta (cota 4, da face da alma); imagens esquematicas nas abas;
//       rotulos numerados; campos dependentes espelham o valor efetivo.
//  v9: chapa de fundo do tubo (circular, mesmo diametro) + solda; janela grande; aba "Welds" (ID 4400).
//  v8: ajuste da ponta inferior do tubo (BeamShortening) ate a base da chapa de conexao + ultrapassagem.
//  v7: local de montagem (eAssemblyLocation) das soldas e dos parafusos - antes era 0 (provavel kWrong/kUnknown),
//      o que impedia a numeracao de agrupar chapa+viga e chapa+tubo.
//  v6: soldas (WeldPoint) ancoragem<->viga e conexao<->tubo; inverter parafusos; aba de parafusos com titulo proprio.
//  v5: parafusos (FinitRectScrewBoltPattern) atravessando a sobreposicao das chapas.
//  v4: altura fixa referenciada ao topo da mesa superior; chapas renomeadas
//      (azul -> ancoragem, amarela -> conexao); opcao "Chapas com alturas iguais".
//
// Geometria (validada com o usuario por imagens, 2026-10-21):
//  - Tubo vertical ao lado da viga, fora da borda das mesas.
//  - Chapa de ANCORAGEM (azul, da viga): perpendicular ao eixo da viga, borda soldada na alma,
//    entre as mesas, avanca alem da borda das mesas em direcao ao tubo.
//  - Chapa de CONEXAO (amarela, do tubo): paralela a azul, encostada nela, com o plano medio
//    passando pelo CENTRO do tubo; vai da regiao de sobreposicao ate a face do tubo.
//  - A ancoragem fica "antes" ou "depois" do tubo ao longo do eixo da viga (parametro).
//
// Convencao do sistema local da viga (PhysCSStart), conferida no log da v2 para viga horizontal:
//  X = eixo da viga, Y = horizontal transversal (largura das mesas), Z = altura do perfil.
//  (Se o perfil estiver girado isso pode mudar - o log registra os eixos para conferir.)
public sealed class HandrailBracketRule : IRule
{
    private const int FilerVersion = 14;   // v14: chaves com nomes legiveis (colunas da Library)

    // Chaves do IFiler = nomes das colunas na Library (RULE_CTT_Handrail). Desde a v14 sao nomes legiveis
    // (sem acento/espaco, por seguranca no SQL e no DWG). Os nomes antigos (_anchorSide, ...) continuam
    // sendo lidos no Load para abrir DWGs e pre-definicoes feitos antes da v14 (ver OldKeys).
    private static class K
    {
        public const string Ancoragem_Posicao = "Ancoragem_Posicao";
        public const string Ancoragem_Espessura = "Ancoragem_Espessura";
        public const string Ancoragem_Altura = "Ancoragem_Altura";
        public const string Ancoragem_DistTopo = "Ancoragem_DistTopo";
        public const string Ancoragem_Largura = "Ancoragem_Largura";
        public const string Alturas_Iguais = "Alturas_Iguais";
        public const string Conexao_Espessura = "Conexao_Espessura";
        public const string Conexao_Altura = "Conexao_Altura";
        public const string Conexao_DistTopo = "Conexao_DistTopo";
        public const string Sobreposicao = "Sobreposicao";
        public const string Parafuso_Norma = "Parafuso_Norma";
        public const string Parafuso_Grau = "Parafuso_Grau";
        public const string Parafuso_Conjunto = "Parafuso_Conjunto";
        public const string Parafuso_Diametro = "Parafuso_Diametro";
        public const string Parafuso_Linhas = "Parafuso_Linhas";
        public const string Parafuso_Colunas = "Parafuso_Colunas";
        public const string Parafuso_EspVertical = "Parafuso_EspVertical";
        public const string Parafuso_EspHorizontal = "Parafuso_EspHorizontal";
        public const string Parafuso_DistTopo = "Parafuso_DistTopo";
        public const string Parafuso_DeslLateral = "Parafuso_DeslLateral";
        public const string Parafuso_Inverter = "Parafuso_Inverter";
        public const string Parafuso_Montagem = "Parafuso_Montagem";
        public const string Solda_Ancoragem_Tipo = "Solda_Ancoragem_Tipo";
        public const string Solda_Ancoragem_Espessura = "Solda_Ancoragem_Espessura";
        public const string Solda_Ancoragem_Local = "Solda_Ancoragem_Local";
        public const string Solda_Conexao_Tipo = "Solda_Conexao_Tipo";
        public const string Solda_Conexao_Espessura = "Solda_Conexao_Espessura";
        public const string Solda_Conexao_Local = "Solda_Conexao_Local";
        public const string Tubo_AjustarPonta = "Tubo_AjustarPonta";
        public const string Tubo_Ultrapassagem = "Tubo_Ultrapassagem";
        public const string ChapaFundo = "ChapaFundo";
        public const string ChapaFundo_Espessura = "ChapaFundo_Espessura";
    }

    private static readonly Dictionary<string, string> OldKeys = new()
    {
        [K.Ancoragem_Posicao] = "_anchorSide",
        [K.Ancoragem_Espessura] = "_anchorThickness",
        [K.Ancoragem_Altura] = "_anchorHeight",
        [K.Ancoragem_DistTopo] = "_anchorTopOffset",
        [K.Ancoragem_Largura] = "_anchorWidth",
        [K.Alturas_Iguais] = "_equalHeights",
        [K.Conexao_Espessura] = "_connThickness",
        [K.Conexao_Altura] = "_connHeight",
        [K.Conexao_DistTopo] = "_connTopOffset",
        [K.Sobreposicao] = "_overlap",
        [K.Parafuso_Norma] = "_boltStandard",
        [K.Parafuso_Grau] = "_boltGrade",
        [K.Parafuso_Conjunto] = "_boltAssembly",
        [K.Parafuso_Diametro] = "_boltDiameter",
        [K.Parafuso_Linhas] = "_boltNy",
        [K.Parafuso_Colunas] = "_boltNx",
        [K.Parafuso_EspVertical] = "_boltDy",
        [K.Parafuso_EspHorizontal] = "_boltDx",
        [K.Parafuso_DistTopo] = "_boltTopEdge",
        [K.Parafuso_DeslLateral] = "_boltLateralOffset",
        [K.Parafuso_Inverter] = "_boltInvert",
        [K.Parafuso_Montagem] = "_boltLocation",
        [K.Solda_Ancoragem_Tipo] = "_weldAnchorType",
        [K.Solda_Ancoragem_Espessura] = "_weldAnchorSize",
        [K.Solda_Ancoragem_Local] = "_weldAnchorLocation",
        [K.Solda_Conexao_Tipo] = "_weldConnType",
        [K.Solda_Conexao_Espessura] = "_weldConnSize",
        [K.Solda_Conexao_Local] = "_weldConnLocation",
        [K.Tubo_AjustarPonta] = "_tubeAdjust",
        [K.Tubo_Ultrapassagem] = "_tubeOvershoot",
        [K.ChapaFundo] = "_capPlate",
        [K.ChapaFundo_Espessura] = "_capThickness",
    };

    // Imagens das abas (recursos embutidos; LogicalName no .csproj termina com estes nomes).
    // Nomes unicos porque o BitmapImageLoader do Advance Steel faz cache SO pelo nome da imagem.
    private const string LayoutImage = "CTT_Handrail_Layout.png";
    private const string BoltsImage = "CTT_Handrail_Bolts.png";

    // Chapa de ANCORAGEM (soldada na viga; era a "azul")
    private int _anchorSide = 0;                 // 0 = antes do tubo, 1 = depois do tubo
    private double _anchorThickness = 10;
    private double _anchorHeight = 100;
    private double _anchorTopOffset = 15;        // distancia do TOPO da mesa superior ao topo da chapa
    private double _anchorProjection = 40;       // OBSOLETO (v<=9): usado so para migrar para _anchorWidth
    private double _anchorWidth = -1;            // cota 4: largura da chapa a partir da face da alma (-1 = migrar)

    // Chapa de CONEXAO (soldada no tubo; era a "amarela")
    private int _equalHeights = 1;               // 1 = mesma altura/posicao da chapa de ancoragem
    private double _connThickness = 10;
    private double _connHeight = 100;            // usado so se _equalHeights = 0
    private double _connTopOffset = 15;          // usado so se _equalHeights = 0
    // v11: valores proprios da chapa de conexao, guardados enquanto "alturas iguais" esta marcado,
    // para serem restaurados quando o usuario desmarcar (os campos 6/7 mostram o espelho da ancoragem).
    private double _connHeightOwn = 100;
    private double _connTopOffsetOwn = 15;
    private int _equalHeightsPrev = 1;
    private double _overlap = 80;                // sobreposicao entre as chapas (regiao dos parafusos)

    // PARAFUSOS (paralelos ao eixo da viga, na regiao de sobreposicao)
    private string _boltStandard = "";           // vazio = usa o padrao do Advance Steel e grava o valor obtido
    private string _boltGrade = "";
    private string _boltAssembly = "";
    private double _boltDiameter = 0;            // 0 = padrao do Advance Steel
    private int _boltNx = 1;                     // colunas (direcao transversal a viga)
    private int _boltNy = 2;                     // linhas (direcao vertical)
    private double _boltDx = 50;                 // espacamento entre colunas
    private double _boltDy = 60;                 // espacamento entre linhas
    private double _boltTopEdge = 30;            // do topo da regiao comum das chapas ate a 1a linha
    private double _boltLateralOffset = 0;       // deslocamento lateral a partir do centro da sobreposicao
    private int _boltInvert = 0;                 // 1 = inverte o lado da cabeca/porca (BoltPattern.IsInverted)

    // SOLDAS (tipo = valor de WeldPattern.eWeldType escolhido no seletor nativo)
    private int _weldAnchorType = (int)WeldPattern.eWeldType.kSlantWeld;  // ancoragem <-> viga (alma)
    private double _weldAnchorSize = 5;
    private int _weldConnType = (int)WeldPattern.eWeldType.kSlantWeld;    // conexao <-> tubo
    private double _weldConnSize = 5;
    private int _weldAnchorLocation = 0;         // indice em LocationOptions (0 = fabrica)
    private int _weldConnLocation = 0;
    private int _boltLocation = 1;               // 1 = obra

    // TUBO: ponta inferior ajustada ate a base da chapa de conexao, ultrapassando X mm
    private int _tubeAdjust = 1;                 // 1 = ajusta (estende ou corta) a ponta inferior do tubo
    private double _tubeOvershoot = 10;          // quanto o tubo passa abaixo da base da chapa de conexao
    private int _capPlate = 0;                   // 1 = cria chapa circular no fundo do tubo
    private double _capThickness = 6;

    // Opcoes do seletor "local de montagem" -> enum oficial (herdado da classe base de WeldPattern/BoltPattern)
    private static readonly string[] LocationLabels = { "Fábrica (In shop)", "Obra (On site)", "Furação na obra (Site drill)" };
    private static readonly WeldPattern.eAssemblyLocation[] LocationValues =
    {
        WeldPattern.eAssemblyLocation.kInShop,
        WeldPattern.eAssemblyLocation.kOnSite,
        WeldPattern.eAssemblyLocation.kSiteDrill,
    };

    private static WeldPattern.eAssemblyLocation ToLocation(int index)
        => index >= 0 && index < LocationValues.Length ? LocationValues[index] : WeldPattern.eAssemblyLocation.kInShop;

    public ObjectId JointId { get; set; }

    // ------------------------------------------------------------------ Query
    public void Query(IUI ui)
    {
        Log.Write($"Handrail.Query: inicio JointId={JointId}");
        try
        {
            var filter = ui.ClassFilter;
            filter.Reset();
            filter.AppendAcceptedBaseClass((FilerObject.eObjectType)200);

            ui.PrintStringMessage("\nConnectionTT: selecione a VIGA");
            FilerObject beam = ui.AcquireSingleObjectWithPoint(86800, out eUIErrorCodes beamErr, out Point3d _);
            Log.Write($"Handrail.Query: viga={Describe(beam)} err={beamErr}");
            if (beam == null) { Log.Write("Handrail.Query: viga nula - saindo"); return; }

            ui.PrintStringMessage("\nConnectionTT: selecione o TUBO do corrimao");
            FilerObject tube = ui.AcquireSingleObjectWithPoint(86800, out eUIErrorCodes tubeErr, out Point3d _);
            Log.Write($"Handrail.Query: tubo={Describe(tube)} err={tubeErr}");
            if (tube == null) { Log.Write("Handrail.Query: tubo nulo - saindo"); return; }

            if (DatabaseManager.Open(JointId) is not IJoint joint)
            {
                Log.Write("Handrail.Query: Open(JointId) NAO retornou IJoint");
                return;
            }

            joint.SetInputObjects(new[] { beam, tube }, new[] { -1, -1 });
            Log.Write("Handrail.Query: SetInputObjects OK (viga, tubo)");
        }
        catch (Exception ex)
        {
            Log.Write("Handrail.Query EXCEPTION: " + ex);
            throw;
        }
    }

    // ------------------------------------------------------------------ CreateObjects
    public void CreateObjects()
    {
        Log.Write("Handrail.CreateObjects: inicio " + ParamsText());
        var created = new List<CreatedObjectInformation>();
        IJoint? joint = null;
        try
        {
            joint = DatabaseManager.Open(JointId) as IJoint;
            if (joint == null) { Log.Write("Handrail.CreateObjects: Open(JointId) NAO retornou IJoint"); return; }

            var inputs = joint.InputObjects.ToList();
            Log.Write($"Handrail.CreateObjects: {inputs.Count} input(s): " + string.Join(", ", inputs.Select(Describe)));
            if (inputs.Count < 2 || inputs[0] is not Beam beam || inputs[1] is not Beam tube)
            {
                Log.Write("Handrail.CreateObjects: esperado [Beam viga, Beam tubo] - saindo");
                return;
            }

            // --- Sistema local da viga
            beam.PhysCSStart.GetCoordSystem(out Point3d o, out Vector3d bx, out Vector3d by, out Vector3d bz);
            Log.Write($"Handrail.CS viga: O={P(o)} X={V(bx)} Y={V(by)} Z={V(bz)}");

            // --- Dimensoes dos perfis
            var beamDims = ProfileDims.Read(beam, "viga");
            var tubeDims = ProfileDims.Read(tube, "tubo");
            double H = beamDims.Get("H", 200), B = beamDims.Get("B", 100), tw = beamDims.Get("Tw", 6), tf = beamDims.Get("Tf", 9);
            double D = tubeDims.Get("D", tubeDims.Get("H", 48.3));
            double R = D / 2.0;
            Log.Write($"Handrail.Dims: viga H={H} B={B} tw={tw} tf={tf} | tubo D={D}");

            // --- Posicao do tubo no sistema da viga
            tube.PhysCSStart.GetCoordSystem(out Point3d to, out Vector3d tx, out Vector3d _, out Vector3d _);
            double a = Dot(Sub(to, o), bx);     // posicao ao longo da viga
            double s = Dot(Sub(to, o), by);     // afastamento lateral (sinal = lado do tubo)
            double sigma = s >= 0 ? 1.0 : -1.0;
            double dist = Math.Abs(s);
            Log.Write($"Handrail.Tubo: O={P(to)} eixo={V(tx)} a={a:F1} s={s:F1} lado={(sigma > 0 ? "+Y" : "-Y")} " +
                      $"(eixo tubo . eixo viga = {Dot(tx, bx):F3}; esperado ~0)");

            if (dist - R <= B / 2.0)
            {
                Log.Write($"Handrail.AVISO: face do tubo ({dist - R:F1}) esta dentro da largura da mesa ({B / 2.0:F1}) - geometria pode colidir");
            }

            // --- Alturas (Z local). Origem do CS da viga no centro da secao (confirmado no log v3: O.z = -H/2),
            //     entao o topo da mesa superior esta em z = +H/2.
            double zTop = H / 2.0;
            double hAnchor = _anchorHeight;
            double zcAnchor = zTop - _anchorTopOffset - hAnchor / 2.0;
            bool equal = _equalHeights != 0;
            bool prevEqual = _equalHeightsPrev != 0;
            if (equal)
            {
                // A API nao permite desabilitar campos; os campos 6/7 passam a mostrar o valor efetivo (espelho).
                // Ao MARCAR agora, guarda antes os valores proprios da conexao para restaurar depois.
                if (!prevEqual)
                {
                    _connHeightOwn = _connHeight;
                    _connTopOffsetOwn = _connTopOffset;
                    Log.Write($"Handrail.alturasIguais: marcado -> guardando conexao h={_connHeightOwn} dTopo={_connTopOffsetOwn}");
                }
                _connHeight = _anchorHeight;
                _connTopOffset = _anchorTopOffset;
            }
            else if (prevEqual && Math.Abs(_connHeight - _anchorHeight) < 1e-6 && Math.Abs(_connTopOffset - _anchorTopOffset) < 1e-6)
            {
                // DESMARCADO agora: os campos ainda contem o espelho da ancoragem -> restaura os valores proprios.
                Log.Write($"Handrail.alturasIguais: desmarcado -> restaurando conexao h={_connHeightOwn} dTopo={_connTopOffsetOwn} (campo tinha h={_connHeight} dTopo={_connTopOffset})");
                _connHeight = _connHeightOwn;
                _connTopOffset = _connTopOffsetOwn;
            }
            else
            {
                // Independente: o que o usuario digitou e o valor proprio.
                _connHeightOwn = _connHeight;
                _connTopOffsetOwn = _connTopOffset;
            }
            _equalHeightsPrev = equal ? 1 : 0;
            double hConn = equal ? hAnchor : _connHeight;
            double zcConn = equal ? zcAnchor : zTop - _connTopOffset - hConn / 2.0;
            CheckVertical("ANCORAGEM", zcAnchor, hAnchor, H, tf);
            CheckVertical("CONEXAO", zcConn, hConn, H, tf);

            // --- Faixas laterais (Y local, no lado do tubo)
            double yWeb = tw / 2.0;                         // face da alma
            if (_anchorWidth <= 0)
            {
                // Migracao de conexoes antigas: preserva a geometria que existia (borda da mesa + avanco).
                _anchorWidth = B / 2.0 + _anchorProjection - tw / 2.0;
                Log.Write($"Handrail.Migracao: largura da ancoragem calculada a partir do avanco antigo = {_anchorWidth:F1}");
            }
            double yAnchorEnd = yWeb + _anchorWidth;           // ponta da ancoragem (cota 4 a partir da face da alma)
            double yTubeFace = dist - R;                    // face do tubo
            double yConnStart = yAnchorEnd - _overlap;      // inicio da conexao (sobreposicao)

            // --- Posicoes ao longo da viga (X local)
            double xConn = a;                                                     // plano medio passa no centro do tubo
            double dir = _anchorSide == 0 ? -1.0 : 1.0;                           // antes (-X) / depois (+X)
            double xAnchor = a + dir * (_connThickness / 2.0 + _anchorThickness / 2.0);

            Log.Write($"Handrail.Layout: xAncoragem={xAnchor:F1} xConexao={xConn:F1} yAlma={yWeb:F1} yPontaAncoragem={yAnchorEnd:F1} " +
                      $"yInicioConexao={yConnStart:F1} yFaceTubo={yTubeFace:F1} zTopoMesa={zTop:F1} " +
                      $"ancoragem[h={hAnchor:F1} zc={zcAnchor:F1}] conexao[h={hConn:F1} zc={zcConn:F1} iguais={equal}]");

            var anchor = MakePlate(o, bx, by, bz, xAnchor, sigma, yWeb, yAnchorEnd, zcAnchor, hAnchor, _anchorThickness, "ANCORAGEM");
            created.Add(MakeInfo(anchor, "BeamPlate"));   // id de transferencia mantido da v3 (estabilidade)

            var conn = MakePlate(o, bx, by, bz, xConn, sigma, yConnStart, yTubeFace, zcConn, hConn, _connThickness, "CONEXAO");
            created.Add(MakeInfo(conn, "TubePlate"));      // id de transferencia mantido da v3 (estabilidade)

            // --- Parafusos
            double topCommon = Math.Min(zcAnchor + hAnchor / 2, zcConn + hConn / 2);
            double bottomCommon = Math.Max(zcAnchor - hAnchor / 2, zcConn - hConn / 2);
            double yOverlapMid = (yConnStart + yAnchorEnd) / 2.0;
            double xContact = a + dir * (_connThickness / 2.0);   // face de contato entre as chapas
            var bolts = MakeBolts(o, bx, by, bz, sigma, xContact, yOverlapMid, yConnStart, yAnchorEnd,
                                  topCommon, bottomCommon, anchor, conn);
            if (bolts != null)
            {
                created.Add(MakeInfo(bolts, "Bolts"));
            }

            // --- Soldas
            // Ancoragem <-> viga: ponto na borda da chapa encostada na alma, a meia altura da chapa.
            var wAnchor = MakeWeld(o, bx, by, bz, sigma, xAnchor, yWeb, zcAnchor,
                                   new FilerObject[] { beam, anchor }, _weldAnchorType, _weldAnchorSize, ToLocation(_weldAnchorLocation), "ANCORAGEM-VIGA");
            if (wAnchor != null) created.Add(MakeInfo(wAnchor, "WeldAnchorBeam"));

            // Conexao <-> tubo: ponto na ponta da chapa de conexao, na face do tubo.
            var wConn = MakeWeld(o, bx, by, bz, sigma, xConn, yTubeFace, zcConn,
                                 new FilerObject[] { tube, conn }, _weldConnType, _weldConnSize, ToLocation(_weldConnLocation), "CONEXAO-TUBO");
            if (wConn != null) created.Add(MakeInfo(wConn, "WeldConnTube"));

            // --- Ponta inferior do tubo
            double zTarget = (zcConn - hConn / 2.0) - _tubeOvershoot;   // base da chapa de conexao - ultrapassagem
            var sh = AdjustTubeEnd(tube, o, bz, zTarget, _tubeAdjust != 0, out Point3d tubeBottom, out Vector3d tubeOut);
            if (sh != null) created.Add(MakeInfo(sh, "TubeEnd"));

            // --- Chapa de fundo do tubo (circular, mesmo diametro externo do tubo)
            if (_capPlate != 0)
            {
                var cap = MakeCapPlate(tubeBottom, tubeOut, D / 2.0, _capThickness);
                created.Add(MakeInfo(cap, "TubeCap"));
                var wCap = MakeWeld(tubeBottom, tubeOut, new FilerObject[] { tube, cap },
                                    _weldConnType, _weldConnSize, ToLocation(_weldConnLocation), "FUNDO-TUBO");
                if (wCap != null) created.Add(MakeInfo(wCap, "WeldCapTube"));
            }
        }
        catch (Exception ex)
        {
            Log.Write("Handrail.CreateObjects EXCEPTION: " + ex);
            throw;
        }
        finally
        {
            if (joint != null)
            {
                try
                {
                    joint.SetCreatedObjects(created, true);
                    Log.Write($"Handrail.CreateObjects: SetCreatedObjects OK ({created.Count} objeto(s))");
                }
                catch (Exception ex)
                {
                    Log.Write("Handrail.CreateObjects SetCreatedObjects EXCEPTION: " + ex);
                    throw;
                }
            }
        }
    }

    private FinitRectScrewBoltPattern? MakeBolts(Point3d o, Vector3d bx, Vector3d by, Vector3d bz, double sigma,
        double xContact, double yMid, double yMin, double yMax, double zTopCommon, double zBottomCommon,
        Plate anchor, Plate conn)
    {
        int nx = Math.Max(1, _boltNx), ny = Math.Max(1, _boltNy);
        double width = (nx - 1) * _boltDx, height = (ny - 1) * _boltDy;
        double yFirst = yMid + _boltLateralOffset - width / 2.0, yLast = yFirst + width;
        double zFirst = zTopCommon - _boltTopEdge, zLast = zFirst - height;   // de cima para baixo

        if (yFirst < yMin || yLast > yMax)
            Log.Write($"Handrail.AVISO: parafusos (y {yFirst:F1}..{yLast:F1}) fora da sobreposicao (y {yMin:F1}..{yMax:F1})");
        if (zFirst > zTopCommon || zLast < zBottomCommon)
            Log.Write($"Handrail.AVISO: parafusos (z {zLast:F1}..{zFirst:F1}) fora da altura comum das chapas (z {zBottomCommon:F1}..{zTopCommon:F1})");

        Point3d L(double y, double z) => new Point3d(
            o.x + bx.x * xContact + by.x * sigma * y + bz.x * z,
            o.y + bx.y * xContact + by.y * sigma * y + bz.y * z,
            o.z + bx.z * xContact + by.z * sigma * y + bz.z * z);

        var xDir = new Vector3d(by.x * sigma, by.y * sigma, by.z * sigma);  // transversal (lado do tubo)
        var yDir = new Vector3d(-bz.x, -bz.y, -bz.z);                       // vertical, de cima para baixo
        var p1 = L(yFirst, zFirst);   // centro do 1o parafuso (canto superior, lado da alma)
        var p2 = L(yLast, zLast);     // centro do ultimo parafuso (canto oposto)

        // HIPOTESE: os dois pontos do construtor sao os centros dos parafusos dos cantos.
        // O log compara o esperado com GetMidpoints() para confirmar.
        var bp = new FinitRectScrewBoltPattern(p1, p2, xDir, yDir);
        bp.Nx = nx;
        bp.Ny = ny;
        if (nx > 1) bp.Dx = _boltDx;
        if (ny > 1) bp.Dy = _boltDy;
        if (!string.IsNullOrEmpty(_boltStandard)) bp.Standard = _boltStandard;
        if (!string.IsNullOrEmpty(_boltGrade)) bp.Grade = _boltGrade;
        if (!string.IsNullOrEmpty(_boltAssembly)) bp.BoltAssembly = _boltAssembly;
        if (_boltDiameter > 0) bp.ScrewDiameter = _boltDiameter;
        bp.IsInverted = _boltInvert != 0;

        var id = bp.WriteToDb();
        bp.Connect(new FilerObject[] { anchor, conn }, ToLocation(_boltLocation));

        // Grava de volta os valores efetivos (preenche o seletor de parafusos na 1a vez)
        _boltStandard = bp.Standard; _boltGrade = bp.Grade; _boltAssembly = bp.BoltAssembly; _boltDiameter = bp.ScrewDiameter;

        string actual;
        try { bp.GetMidpoints(out Point3d[] mids); actual = string.Join(" ", mids.Select(P)); }
        catch (Exception ex) { actual = "GetMidpoints falhou: " + ex.GetType().Name; }
        Log.Write($"Handrail.Parafusos: WriteToDb OK handle={SafeHandle(bp)} id={id} n={bp.NumberOfScrews} ({nx}x{ny}) " +
                  $"norma='{bp.Standard}' grau='{bp.Grade}' conjunto='{bp.BoltAssembly}' d={bp.ScrewDiameter} L={bp.ScrewLength:F1} invertido={bp.IsInverted} local={bp.AssemblyLocation}");
        Log.Write($"Handrail.Parafusos: esperado p1={P(p1)} p2={P(p2)} | real={actual}");
        return bp;
    }

    private static WeldPoint? MakeWeld(Point3d o, Vector3d bx, Vector3d by, Vector3d bz, double sigma,
        double x, double y, double z, FilerObject[] parts, int weldType, double size,
        WeldPattern.eAssemblyLocation location, string name)
    {
        try
        {
            var p = new Point3d(
                o.x + bx.x * x + by.x * sigma * y + bz.x * z,
                o.y + bx.y * x + by.y * sigma * y + bz.y * z,
                o.z + bx.z * x + by.z * sigma * y + bz.z * z);
            var xDir = new Vector3d(bz.x, bz.y, bz.z);                                // ao longo do cordao (vertical)
            var yDir = new Vector3d(by.x * sigma, by.y * sigma, by.z * sigma);        // transversal

            // Mesmo padrao do BridgeGirder.CreateWelds: WeldPoint(ponto, x, y) -> SetWeldType -> WriteToDb -> Connect
            var w = new WeldPoint(p, xDir, yDir);
            w.SetWeldType(WeldPattern.eSeamPosition.kUpper, (WeldPattern.eWeldType)weldType);
            w.SetSeamThickness(WeldPattern.eSeamPosition.kUpper, size);
            var id = w.WriteToDb();
            w.Connect(parts, location);
            Log.Write($"Handrail.Solda {name}: WriteToDb OK handle={SafeHandle(w)} id={id} ponto={P(p)} " +
                      $"tipo={w.GetWeldType(WeldPattern.eSeamPosition.kUpper)} espessura={w.GetSeamThickness(WeldPattern.eSeamPosition.kUpper)} " +
                      $"local={w.AssemblyLocation} pecas={string.Join(",", parts.Select(SafeHandle))}");
            return w;
        }
        catch (Exception ex)
        {
            Log.Write($"Handrail.Solda {name} EXCEPTION: {ex}");
            throw;
        }
    }

    // Estende (ou corta) a ponta INFERIOR do tubo para que ela fique na cota local zTarget (eixo Z da viga).
    // Usa BeamShortening(eEnd, comprimento): positivo encurta, negativo estende (HIPOTESE - conferir no log/modelo).
    private static BeamShortening? AdjustTubeEnd(Beam tube, Point3d o, Vector3d bz, double zTarget)
        => AdjustTubeEnd(tube, o, bz, zTarget, true, out _, out _);

    // apply=false: so calcula a ponta inferior (sem criar o encurtamento).
    private static BeamShortening? AdjustTubeEnd(Beam tube, Point3d o, Vector3d bz, double zTarget, bool apply,
        out Point3d bottom, out Vector3d outward)
    {
        bottom = tube.GetPointAtStart(); outward = new Vector3d(0, 0, -1);
        try
        {
            Point3d ps = tube.GetPointAtStart(), pe = tube.GetPointAtEnd();
            double zs = Dot(Sub(ps, o), bz), ze = Dot(Sub(pe, o), bz);
            bool startIsLower = zs < ze;
            var end = startIsLower ? Beam.eEnd.kStart : Beam.eEnd.kEnd;
            Point3d pLow = startIsLower ? ps : pe, pHigh = startIsLower ? pe : ps;

            var axis = Sub(pLow, pHigh);                       // aponta para fora pela ponta inferior
            double len = Math.Sqrt(Dot(axis, axis));
            if (len < 1e-6) { Log.Write("Handrail.Tubo AVISO: tubo com comprimento zero - ajuste ignorado"); return null; }
            axis = new Vector3d(axis.x / len, axis.y / len, axis.z / len);
            outward = axis;
            bottom = pLow;
            double az = Dot(axis, bz);                         // ~ -1 para tubo vertical
            if (Math.Abs(az) < 0.2) { Log.Write($"Handrail.Tubo AVISO: tubo quase horizontal (az={az:F3}) - ajuste ignorado"); return null; }

            double zLow = Math.Min(zs, ze);
            double extension = (zTarget - zLow) / az;          // > 0 = precisa crescer; < 0 = precisa encurtar
            if (!apply) return null;
            bottom = new Point3d(pLow.x + axis.x * extension, pLow.y + axis.y * extension, pLow.z + axis.z * extension);
            var sh = new BeamShortening(end, -extension);
            tube.AddFeature(sh);
            Log.Write($"Handrail.Tubo: ponta inferior={end} zAtual={zLow:F1} zAlvo={zTarget:F1} " +
                      $"extensao={extension:F1} (shortening={-extension:F1}) handle={SafeHandle(sh)} InsLength={sh.InsLength:F1} " +
                      $"comprimento agora={tube.GetLength():F1}");
            return sh;
        }
        catch (Exception ex)
        {
            Log.Write("Handrail.Tubo EXCEPTION: " + ex);
            throw;
        }
    }

    // Chapa circular encostada na ponta do tubo, por fora (topo da chapa = ponta do tubo).
    private static Plate MakeCapPlate(Point3d tubeEnd, Vector3d outward, double radius, double thickness)
    {
        var center = new Point3d(tubeEnd.x + outward.x * thickness / 2, tubeEnd.y + outward.y * thickness / 2, tubeEnd.z + outward.z * thickness / 2);
        var plane = new Plane(center, outward);
        var cap = new Plate(plane, center, radius); // HIPOTESE: (plano, centro, raio) = chapa circular - conferir no log
        cap.Thickness = thickness;
        cap.Portioning = 0.5;                        // espessura centrada no plano (comportamento ja visto nas outras chapas)
        var id = cap.WriteToDb();
        Log.Write($"Handrail.Chapa FUNDO: WriteToDb OK handle={SafeHandle(cap)} id={id} centro={P(center)} raioPedido={radius:F2} " +
                  $"raio={cap.Radius:F2} retangular={cap.IsRectangular()} t={cap.Thickness}");
        return cap;
    }

    // Solda num ponto qualquer (usada pela chapa de fundo). Mesmo padrao: WeldPoint -> tipo -> WriteToDb -> Connect.
    private static WeldPoint? MakeWeld(Point3d p, Vector3d normal, FilerObject[] parts, int weldType, double size,
        WeldPattern.eAssemblyLocation location, string name)
    {
        try
        {
            var any = Math.Abs(normal.z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
            var xDir = new Vector3d(normal.y * any.z - normal.z * any.y, normal.z * any.x - normal.x * any.z, normal.x * any.y - normal.y * any.x);
            var yDir = new Vector3d(normal.y * xDir.z - normal.z * xDir.y, normal.z * xDir.x - normal.x * xDir.z, normal.x * xDir.y - normal.y * xDir.x);
            var w = new WeldPoint(p, xDir, yDir);
            w.SetWeldType(WeldPattern.eSeamPosition.kUpper, (WeldPattern.eWeldType)weldType);
            w.SetSeamThickness(WeldPattern.eSeamPosition.kUpper, size);
            var id = w.WriteToDb();
            w.Connect(parts, location);
            Log.Write($"Handrail.Solda {name}: WriteToDb OK handle={SafeHandle(w)} id={id} ponto={P(p)} " +
                      $"tipo={w.GetWeldType(WeldPattern.eSeamPosition.kUpper)} espessura={w.GetSeamThickness(WeldPattern.eSeamPosition.kUpper)} " +
                      $"local={w.AssemblyLocation} pecas={string.Join(",", parts.Select(SafeHandle))}");
            return w;
        }
        catch (Exception ex)
        {
            Log.Write($"Handrail.Solda {name} EXCEPTION: {ex}");
            throw;
        }
    }

    private static void CheckVertical(string name, double zc, double h, double H, double tf)
    {
        double top = zc + h / 2, bottom = zc - h / 2;
        double innerTop = H / 2 - tf, innerBottom = -H / 2 + tf;
        if (top > innerTop || bottom < innerBottom)
        {
            Log.Write($"Handrail.AVISO: chapa de {name} (z {bottom:F1}..{top:F1}) invade as mesas (vao livre z {innerBottom:F1}..{innerTop:F1})");
        }
    }

    // Chapa no plano perpendicular ao eixo da viga (normal = X local), na posicao x.
    // Contorno retangular de y0..y1 (lado sigma) e altura h centrada em zc.
    private static Plate MakePlate(Point3d o, Vector3d bx, Vector3d by, Vector3d bz,
                                   double x, double sigma, double y0, double y1,
                                   double zc, double h, double thickness, string name)
    {
        Point3d L(double y, double z) => new Point3d(
            o.x + bx.x * x + by.x * sigma * y + bz.x * z,
            o.y + bx.y * x + by.y * sigma * y + bz.y * z,
            o.z + bx.z * x + by.z * sigma * y + bz.z * z);

        var corners = new[]
        {
            L(y0, zc - h / 2), L(y1, zc - h / 2), L(y1, zc + h / 2), L(y0, zc + h / 2),
        };
        var plane = new Plane(L((y0 + y1) / 2, zc), bx);
        var plate = new Plate(plane, corners, thickness);
        plate.Portioning = 0.5; // HIPOTESE: centraliza a espessura no plano (a confirmar visualmente)
        var id = plate.WriteToDb();
        Log.Write($"Handrail.Chapa {name}: WriteToDb OK handle={SafeHandle(plate)} id={id} " +
                  $"cantos={string.Join(" ", corners.Select(P))} t={thickness}");
        return plate;
    }

    // ------------------------------------------------------------------ UI
    public void GetRulePages(IRuleUIBuilder builder)
    {
        Log.Write("Handrail.GetRulePages");
        builder.SheetPromptId = 97065;
        // v12: sem imagem na aba "Properties". 68258 era a figura herdada do exemplo oficial.
        // Um ID inexistente na tabela de bitmaps faz getBitmapImageFromDbId retornar null (IL confirmado);
        // quem consome este indice e o codigo nativo -> resultado precisa ser confirmado no AS.
        builder.FirstPageBitmapIdx = 0;
        builder.SheetDimension = IRuleUIBuilder.eSheetDimension.kBig;   // janela maior (pedido do usuario)
        // Aba com imagem: BuildRulePage(titulo, grupo, DLL, NOME_DA_IMAGEM) - ordem confirmada no IL (v11):
        //   ASRulePageBuilder.BuildRulePage(t, g, a, b) -> new RulePageTemplateWPF(a, b)
        //   RulePageTemplateWPF(a, b) -> BitmapImageLoader.getBitmapImage(b, a, true)   (ldarg.2, ldarg.1)
        //   getBitmapImage(imagem, dll, _): dll vazia -> sem imagem; File.Exists(imagem) -> carrega arquivo;
        //   senao localiza a DLL (File.Exists ou pastas do AS) e procura recurso embutido que TERMINE com o nome.
        // Na v10 os argumentos estavam invertidos (imagem no lugar da DLL) -> nenhuma imagem.
        // Passamos o caminho completo da propria DLL para nao depender das pastas de busca do Advance Steel.
        string dll = OwnDllPath();
        LogImageResources(dll);
        int page = builder.BuildRulePage(87790, -1, dll, LayoutImage);
        // Layout: imagem com as duas vistas uma embaixo da outra -> coluna da imagem um pouco mais larga.
        AdjustPageColumns(builder, page, "Layout", imageColumn: 230, labelColumn: null);

        // (pagina, ROTULO, CHAVE, tipo) - chave = nome usado no Save/Load. Numeros = cotas das imagens.
        // Obs.: IRuleUIBuilder nao tem API para habilitar/desabilitar campos (confirmado via REA).
        builder.AddSelectorCustom(page, "Posição da ancoragem", K.Ancoragem_Posicao, typeof(int),
            new[] { "Antes do tubo", "Depois do tubo" });
        builder.AddTextBox(page, "1  Ancoragem: espessura", K.Ancoragem_Espessura, typeof(double));
        builder.AddTextBox(page, "2  Ancoragem: altura", K.Ancoragem_Altura, typeof(double));
        builder.AddTextBox(page, "3  Ancoragem: dist. do topo", K.Ancoragem_DistTopo, typeof(double));
        builder.AddTextBox(page, "4  Ancoragem: largura", K.Ancoragem_Largura, typeof(double));
        builder.AddCheckBox(page, "Chapas com alturas iguais", K.Alturas_Iguais);
        builder.AddTextBox(page, "5  Conexão: espessura", K.Conexao_Espessura, typeof(double));
        builder.AddTextBox(page, "6  Conexão: altura", K.Conexao_Altura, typeof(double));
        builder.AddTextBox(page, "7  Conexão: dist. do topo", K.Conexao_DistTopo, typeof(double));
        builder.AddTextBox(page, "8  Sobreposição", K.Sobreposicao, typeof(double));
        builder.AddCheckBox(page, "Ajustar ponta do tubo", K.Tubo_AjustarPonta);
        builder.AddTextBox(page, "9  Tubo: ultrapassagem", K.Tubo_Ultrapassagem, typeof(double));
        builder.AddCheckBox(page, "Chapa no fundo do tubo", K.ChapaFundo);
        builder.AddTextBox(page, "10  Chapa do fundo: espessura", K.ChapaFundo_Espessura, typeof(double));

        // 4200 = "Bolts" no catalogo de textos (AstCrtlDb).
        int boltPage = builder.BuildRulePage(4200, -1, dll, BoltsImage);
        // Bolts: menos espaco para os parametros e mais para a figura.
        AdjustPageColumns(builder, boltPage, "Bolts", imageColumn: 380, labelColumn: 150);
        builder.AddBoltSelector(boltPage, "Parafusos", K.Parafuso_Norma, K.Parafuso_Grau, K.Parafuso_Diametro, K.Parafuso_Conjunto);
        builder.AddTextBox(boltPage, "Linhas (vertical)", K.Parafuso_Linhas, typeof(int));
        builder.AddTextBox(boltPage, "Colunas (horizontal)", K.Parafuso_Colunas, typeof(int));
        builder.AddTextBox(boltPage, "1  Espaçamento vertical", K.Parafuso_EspVertical, typeof(double));
        builder.AddTextBox(boltPage, "Espaçamento horizontal", K.Parafuso_EspHorizontal, typeof(double));
        builder.AddTextBox(boltPage, "3  Dist. do topo à 1ª linha", K.Parafuso_DistTopo, typeof(double));
        builder.AddTextBox(boltPage, "4  Deslocamento lateral", K.Parafuso_DeslLateral, typeof(double));
        builder.AddCheckBox(boltPage, "Inverter parafusos", K.Parafuso_Inverter);
        builder.AddSelectorCustom(boltPage, "Local de montagem", K.Parafuso_Montagem, typeof(int), LocationLabels);

        // Aba de soldas - 4400 = "Welds" no catalogo de textos (AstCrtlDb), mesmo grupo de 4100 "Plates" e 4200 "Bolts".
        // Sem imagem (pedido do usuario): DLL vazia -> getBitmapImage retorna null logo no inicio (IsNullOrWhiteSpace).
        int weldPage = builder.BuildRulePage(4400, -1, "", "");
        // Welds: sem imagem -> devolve a largura da coluna da imagem para os parametros.
        AdjustPageColumns(builder, weldPage, "Welds", imageColumn: 10, labelColumn: null);
        builder.AddSelector(weldPage, IRuleUIBuilder.eSelectorType.kWeld, "Ancoragem ↔ viga: tipo", K.Solda_Ancoragem_Tipo, Enumerable.Empty<string>());
        builder.AddTextBox(weldPage, "Ancoragem ↔ viga: espessura", K.Solda_Ancoragem_Espessura, typeof(double));
        builder.AddSelectorCustom(weldPage, "Ancoragem ↔ viga: weld location", K.Solda_Ancoragem_Local, typeof(int), LocationLabels);
        builder.AddSelector(weldPage, IRuleUIBuilder.eSelectorType.kWeld, "Conexão ↔ tubo: tipo", K.Solda_Conexao_Tipo, Enumerable.Empty<string>());
        builder.AddTextBox(weldPage, "Conexão ↔ tubo: espessura", K.Solda_Conexao_Espessura, typeof(double));
        builder.AddSelectorCustom(weldPage, "Conexão ↔ tubo: weld location", K.Solda_Conexao_Local, typeof(int), LocationLabels);
    }

    // Library nativa (aba Properties > Type / "Save values"): tabela no AstorRules com
    //   [Key] int PK, [Comment] nvarchar (nome da pre-definicao) e UMA COLUNA POR CHAVE do Save/Load.
    // Formato confirmado nas conexoes .NET do proprio Advance Steel (RULE_WallRail, RULE_Monowills_Handrail).
    // A tabela e criada pelo script scripts\library-ctt-handrail.ps1 (reversivel).
    public const string LibraryTable = "RULE_CTT_Handrail";

    public string GetTableName()
    {
        Log.Write("Handrail.GetTableName -> " + LibraryTable);
        return LibraryTable;
    }

    // ------------------------------------------------------------------ Layout das abas (v12)
    // A IRuleUIBuilder nao expoe tamanho das colunas. O template da pagina (ASSteelControls.RulePageTemplateWPF,
    // XAML lido do BAML) e um Grid de 2 colunas: [*] com _mainGridCtrl (rotulos 200 | campos *) e [200] com _imgCtrl
    // (Margin 10, Stretch Uniform). O builder real (ASMgdJoints.ASRulePageBuilder) guarda as paginas no campo
    // privado _pagesById (Dictionary<int, pagina>). Ajustamos as larguras por reflexao.
    // E API interna: se o Advance Steel mudar, so perde o ajuste (loga e segue com o layout padrao).
    private static void AdjustPageColumns(IRuleUIBuilder builder, int pageId, string name, double imageColumn, double? labelColumn)
    {
        try
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var pages = builder.GetType().GetField("_pagesById", bf)?.GetValue(builder) as System.Collections.IDictionary;
            if (pages == null)
            {
                Log.Write($"Handrail.Layout[{name}]: campo _pagesById nao encontrado em {builder.GetType().FullName} - layout padrao");
                return;
            }

            object? page = pages.Contains(pageId) ? pages[pageId] : null;
            if (page == null)
            {
                // Fallback: a pagina recem-criada e a ultima inserida (Dictionary sem remocoes preserva a ordem).
                foreach (System.Collections.DictionaryEntry e in pages) page = e.Value;
                var keys = string.Join(",", pages.Keys.Cast<object>());
                Log.Write($"Handrail.Layout[{name}]: id {pageId} nao e chave de _pagesById (chaves={keys}) - usando a ultima pagina");
            }
            if (page == null) return;

            var type = page.GetType();
            object? img = type.GetField("_imgCtrl", bf)?.GetValue(page);
            object? mainGrid = type.GetField("_mainGridCtrl", bf)?.GetValue(page);
            object? outerGrid = img?.GetType().GetProperty("Parent")?.GetValue(img);

            bool okImg = SetColumnWidth(outerGrid, 1, imageColumn);
            bool okLabel = labelColumn == null || SetColumnWidth(mainGrid, 0, labelColumn.Value);
            Log.Write($"Handrail.Layout[{name}]: pagina={type.Name} colunaImagem={imageColumn}:{(okImg ? "OK" : "FALHOU")} " +
                      $"colunaRotulos={(labelColumn?.ToString() ?? "padrao")}:{(okLabel ? "OK" : "FALHOU")}");
        }
        catch (Exception ex)
        {
            Log.Write($"Handrail.Layout[{name}]: excecao - layout padrao mantido: {ex}");
        }
    }

    private static bool SetColumnWidth(object? grid, int index, double width)
    {
        if (grid?.GetType().GetProperty("ColumnDefinitions")?.GetValue(grid) is not System.Collections.IList cols || cols.Count <= index)
            return false;
        object? col = cols[index];
        var prop = col?.GetType().GetProperty("Width");
        if (col == null || prop == null) return false;
        object? length = Activator.CreateInstance(prop.PropertyType, width);   // System.Windows.GridLength(double)
        prop.SetValue(col, length);
        return true;
    }

    private static string OwnDllPath()
    {
        string path = typeof(HandrailBracketRule).Assembly.Location;
        if (string.IsNullOrWhiteSpace(path))
        {
            // Location vazio so acontece se o assembly foi carregado de bytes; o AS usa o nome + pastas de busca.
            Log.Write("Handrail.Imagens: Assembly.Location vazio - usando 'ConnectionTT.Joints.dll'");
            return "ConnectionTT.Joints.dll";
        }
        return path;
    }

    private static void LogImageResources(string dll)
    {
        try
        {
            var names = typeof(HandrailBracketRule).Assembly.GetManifestResourceNames();
            bool layout = names.Any(n => n.EndsWith(LayoutImage, StringComparison.Ordinal));
            bool bolts = names.Any(n => n.EndsWith(BoltsImage, StringComparison.Ordinal));
            Log.Write($"Handrail.Imagens: dll='{dll}' existe={System.IO.File.Exists(dll)} recursos=[{string.Join(", ", names)}] " +
                      $"layout={(layout ? "OK" : "FALTANDO")} bolts={(bolts ? "OK" : "FALTANDO")}");
        }
        catch (Exception ex)
        {
            Log.Write("Handrail.Imagens: falha ao listar recursos: " + ex);
        }
    }

    // ------------------------------------------------------------------ Persistencia
    public void Save(IFiler filer)
    {
        filer.WriteVersion(FilerVersion);
        filer.WriteItem(_anchorSide, K.Ancoragem_Posicao);
        filer.WriteItem(_anchorThickness, K.Ancoragem_Espessura);
        filer.WriteItem(_anchorHeight, K.Ancoragem_Altura);
        filer.WriteItem(_anchorTopOffset, K.Ancoragem_DistTopo);
        filer.WriteItem(_anchorProjection, "_anchorProjection");
        filer.WriteItem(_equalHeights, K.Alturas_Iguais);
        filer.WriteItem(_connThickness, K.Conexao_Espessura);
        filer.WriteItem(_connHeight, K.Conexao_Altura);
        filer.WriteItem(_connTopOffset, K.Conexao_DistTopo);
        filer.WriteItem(_overlap, K.Sobreposicao);
        filer.WriteItem(_boltStandard, K.Parafuso_Norma);
        filer.WriteItem(_boltGrade, K.Parafuso_Grau);
        filer.WriteItem(_boltAssembly, K.Parafuso_Conjunto);
        filer.WriteItem(_boltDiameter, K.Parafuso_Diametro);
        filer.WriteItem(_boltNx, K.Parafuso_Colunas);
        filer.WriteItem(_boltNy, K.Parafuso_Linhas);
        filer.WriteItem(_boltDx, K.Parafuso_EspHorizontal);
        filer.WriteItem(_boltDy, K.Parafuso_EspVertical);
        filer.WriteItem(_boltTopEdge, K.Parafuso_DistTopo);
        filer.WriteItem(_boltLateralOffset, K.Parafuso_DeslLateral);
        filer.WriteItem(_boltInvert, K.Parafuso_Inverter);
        filer.WriteItem(_weldAnchorType, K.Solda_Ancoragem_Tipo);
        filer.WriteItem(_weldAnchorSize, K.Solda_Ancoragem_Espessura);
        filer.WriteItem(_weldConnType, K.Solda_Conexao_Tipo);
        filer.WriteItem(_weldConnSize, K.Solda_Conexao_Espessura);
        filer.WriteItem(_weldAnchorLocation, K.Solda_Ancoragem_Local);
        filer.WriteItem(_weldConnLocation, K.Solda_Conexao_Local);
        filer.WriteItem(_boltLocation, K.Parafuso_Montagem);
        filer.WriteItem(_tubeAdjust, K.Tubo_AjustarPonta);
        filer.WriteItem(_tubeOvershoot, K.Tubo_Ultrapassagem);
        filer.WriteItem(_capPlate, K.ChapaFundo);
        filer.WriteItem(_capThickness, K.ChapaFundo_Espessura);
        filer.WriteItem(_anchorWidth, K.Ancoragem_Largura);
        filer.WriteItem(_connHeightOwn, "_connHeightOwn");
        filer.WriteItem(_connTopOffsetOwn, "_connTopOffsetOwn");
        filer.WriteItem(_equalHeightsPrev, "_equalHeightsPrev");
        Log.Write("Handrail.Save: v" + FilerVersion + " " + ParamsText());
    }

    public void Load(IFiler filer)
    {
        // Vindo da Library o filer pode nao ter versao (so as colunas da tabela). Se as chaves atuais
        // existem, trata como formato atual; chaves ausentes mantem o valor que ja estava no campo.
        int version;
        try
        {
            version = filer.ReadVersion();
        }
        catch (Exception ex)
        {
            Log.Write($"Handrail.Load: sem versao no filer ({ex.GetType().Name})");
            version = -1;
        }

        if (version < 0 && HasItem(filer, K.Ancoragem_Espessura) && HasItem(filer, K.Parafuso_Colunas))
        {
            Log.Write($"Handrail.Load: versao {version} com chaves atuais (Library?) - lendo como v{FilerVersion}");
            version = FilerVersion;
        }

        if (version < 3)
        {
            _anchorThickness = ReadDouble(filer, "_plateThickness", _anchorThickness);
            Log.Write($"Handrail.Load: migrando v{version} -> v{FilerVersion}");
        }
        else if (version == 3)
        {
            // v3 usava nomes azul/amarela; altura automatica (0) nao existe mais -> mantem o padrao.
            _anchorSide = ReadInt(filer, "_beamPlateSide", _anchorSide);
            _anchorThickness = ReadDouble(filer, "_beamPlateThickness", _anchorThickness);
            _anchorProjection = ReadDouble(filer, "_beamPlateProjection", _anchorProjection);
            _connThickness = ReadDouble(filer, "_tubePlateThickness", _connThickness);
            _overlap = ReadDouble(filer, K.Sobreposicao, _overlap);
            double h3 = ReadDouble(filer, "_beamPlateHeight", 0);
            if (h3 > 0) _anchorHeight = h3;
            Log.Write($"Handrail.Load: migrando v3 -> v{FilerVersion}");
        }
        else
        {
            _anchorSide = ReadInt(filer, K.Ancoragem_Posicao, _anchorSide);
            _anchorThickness = ReadDouble(filer, K.Ancoragem_Espessura, _anchorThickness);
            _anchorHeight = ReadDouble(filer, K.Ancoragem_Altura, _anchorHeight);
            _anchorTopOffset = ReadDouble(filer, K.Ancoragem_DistTopo, _anchorTopOffset);
            _anchorProjection = ReadDouble(filer, "_anchorProjection", _anchorProjection);
            _equalHeights = ReadInt(filer, K.Alturas_Iguais, _equalHeights);
            _connThickness = ReadDouble(filer, K.Conexao_Espessura, _connThickness);
            _connHeight = ReadDouble(filer, K.Conexao_Altura, _connHeight);
            _connTopOffset = ReadDouble(filer, K.Conexao_DistTopo, _connTopOffset);
            _overlap = ReadDouble(filer, K.Sobreposicao, _overlap);
            if (version >= 5)
            {
                _boltStandard = ReadString(filer, K.Parafuso_Norma, _boltStandard);
                _boltGrade = ReadString(filer, K.Parafuso_Grau, _boltGrade);
                _boltAssembly = ReadString(filer, K.Parafuso_Conjunto, _boltAssembly);
                _boltDiameter = ReadDouble(filer, K.Parafuso_Diametro, _boltDiameter);
                _boltNx = ReadInt(filer, K.Parafuso_Colunas, _boltNx);
                _boltNy = ReadInt(filer, K.Parafuso_Linhas, _boltNy);
                _boltDx = ReadDouble(filer, K.Parafuso_EspHorizontal, _boltDx);
                _boltDy = ReadDouble(filer, K.Parafuso_EspVertical, _boltDy);
                _boltTopEdge = ReadDouble(filer, K.Parafuso_DistTopo, _boltTopEdge);
                _boltLateralOffset = ReadDouble(filer, K.Parafuso_DeslLateral, _boltLateralOffset);
            }
            if (version >= 6)
            {
                _boltInvert = ReadInt(filer, K.Parafuso_Inverter, _boltInvert);
                _weldAnchorType = ReadInt(filer, K.Solda_Ancoragem_Tipo, _weldAnchorType);
                _weldAnchorSize = ReadDouble(filer, K.Solda_Ancoragem_Espessura, _weldAnchorSize);
                _weldConnType = ReadInt(filer, K.Solda_Conexao_Tipo, _weldConnType);
                _weldConnSize = ReadDouble(filer, K.Solda_Conexao_Espessura, _weldConnSize);
            }
            if (version >= 7)
            {
                _weldAnchorLocation = ReadInt(filer, K.Solda_Ancoragem_Local, _weldAnchorLocation);
                _weldConnLocation = ReadInt(filer, K.Solda_Conexao_Local, _weldConnLocation);
                _boltLocation = ReadInt(filer, K.Parafuso_Montagem, _boltLocation);
            }
            if (version >= 8)
            {
                _tubeAdjust = ReadInt(filer, K.Tubo_AjustarPonta, _tubeAdjust);
                _tubeOvershoot = ReadDouble(filer, K.Tubo_Ultrapassagem, _tubeOvershoot);
            }
            if (version >= 9)
            {
                _capPlate = ReadInt(filer, K.ChapaFundo, _capPlate);
                _capThickness = ReadDouble(filer, K.ChapaFundo_Espessura, _capThickness);
            }
            if (version >= 10)
            {
                _anchorWidth = ReadDouble(filer, K.Ancoragem_Largura, _anchorWidth);
            }
            if (version >= 11)
            {
                _connHeightOwn = ReadDouble(filer, "_connHeightOwn", _connHeight);
                _connTopOffsetOwn = ReadDouble(filer, "_connTopOffsetOwn", _connTopOffset);
                _equalHeightsPrev = ReadInt(filer, "_equalHeightsPrev", _equalHeights);
            }
        }
        if (version < 11)
        {
            // Migracao: sem historico -> estado anterior = estado atual (nao restaura nada indevidamente).
            _connHeightOwn = _connHeight;
            _connTopOffsetOwn = _connTopOffset;
            _equalHeightsPrev = _equalHeights;
        }
        Log.Write($"Handrail.Load: v{version} " + ParamsText());
    }

    private string ParamsText() =>
        $"ancoragem[lado={(_anchorSide == 0 ? "antes" : "depois")} t={_anchorThickness} h={_anchorHeight} " +
        $"dTopo={_anchorTopOffset} largura={_anchorWidth}] alturasIguais={_equalHeights}(anterior={_equalHeightsPrev}) " +
        $"conexaoPropria[h={_connHeightOwn} dTopo={_connTopOffsetOwn}] " +
        $"conexao[t={_connThickness} h={_connHeight} dTopo={_connTopOffset}] sobreposicao={_overlap} " +
        $"parafusos[{_boltNx}x{_boltNy} dx={_boltDx} dy={_boltDy} topo={_boltTopEdge} lat={_boltLateralOffset} " +
        $"norma='{_boltStandard}' grau='{_boltGrade}' conj='{_boltAssembly}' d={_boltDiameter} inv={_boltInvert}] " +
        $"soldas[ancoragem tipo={(WeldPattern.eWeldType)_weldAnchorType} a={_weldAnchorSize} | conexao tipo={(WeldPattern.eWeldType)_weldConnType} a={_weldConnSize}] " +
        $"local[soldaAncoragem={ToLocation(_weldAnchorLocation)} soldaConexao={ToLocation(_weldConnLocation)} parafusos={ToLocation(_boltLocation)}] tubo[ajustar={_tubeAdjust} ultrapassa={_tubeOvershoot} chapaFundo={_capPlate} tFundo={_capThickness}]";

    private static bool HasItem(IFiler filer, string key) => ReadRaw(filer, key) != null;

    // Le a chave atual; se nao existir, tenta o nome antigo (antes da v14).
    private static object? ReadRaw(IFiler filer, string key)
    {
        object? v = TryReadItem(filer, key);
        if (v == null && OldKeys.TryGetValue(key, out var old))
        {
            v = TryReadItem(filer, old);
        }
        return v;
    }

    private static object? TryReadItem(IFiler filer, string key)
    {
        try { return filer.ReadItem(key); }
        catch (Exception) { return null; }   // chave ausente: o filer lanca ou devolve null - tratado como "nao tem"
    }

    private static double ReadDouble(IFiler filer, string key, double fallback)
    {
        var v = ReadRaw(filer, key);
        if (v == null) { Log.Write($"Handrail.Load: '{key}' ausente - usando {fallback}"); return fallback; }
        try { return Convert.ToDouble(v); }
        catch (Exception ex) { Log.Write($"Handrail.Load: '{key}' invalida ({ex.GetType().Name}: {v}) - usando {fallback}"); return fallback; }
    }

    private static string ReadString(IFiler filer, string key, string fallback)
    {
        var v = ReadRaw(filer, key);
        if (v == null) { Log.Write($"Handrail.Load: '{key}' ausente - usando '{fallback}'"); return fallback; }
        return v as string ?? fallback;
    }

    private static int ReadInt(IFiler filer, string key, int fallback)
    {
        var v = ReadRaw(filer, key);
        if (v == null) { Log.Write($"Handrail.Load: '{key}' ausente - usando {fallback}"); return fallback; }
        try { return Convert.ToInt32(v); }
        catch (Exception ex) { Log.Write($"Handrail.Load: '{key}' invalida ({ex.GetType().Name}: {v}) - usando {fallback}"); return fallback; }
    }

    // ------------------------------------------------------------------ Utilitarios
    private static CreatedObjectInformation MakeInfo(FilerObject obj, string transferId)
    {
        var info = new CreatedObjectInformation();
        info.CreatedObject = obj;
        info.JointTransferId = transferId;
        info.Attributes = EmptyLike(info.Attributes);
        return info;
    }

    private static T[] EmptyLike<T>(IEnumerable<T>? _) => Array.Empty<T>();

    private static Vector3d Sub(Point3d p, Point3d q) => new Vector3d(p.x - q.x, p.y - q.y, p.z - q.z);
    private static double Dot(Vector3d u, Vector3d v) => u.x * v.x + u.y * v.y + u.z * v.z;
    private static string P(Point3d p) => $"({p.x:F1},{p.y:F1},{p.z:F1})";
    private static string V(Vector3d v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

    private static string Describe(FilerObject? obj)
        => obj == null ? "null" : $"{obj.GetType().Name}(handle={SafeHandle(obj)})";

    private static string SafeHandle(FilerObject obj)
    {
        try { return obj.Handle; }
        catch (Exception ex) { return "?" + ex.GetType().Name; }
    }
}

// Le as dimensoes do perfil via ProfilesManager/ProfileType (APIs confirmadas no ASProfilesMgd.dll via REA).
internal sealed class ProfileDims
{
    private readonly Dictionary<string, double> _values = new(StringComparer.OrdinalIgnoreCase);

    public double Get(string key, double fallback) => _values.TryGetValue(key, out var v) ? v : fallback;

    public static ProfileDims Read(Beam beam, string label)
    {
        var dims = new ProfileDims();
        try
        {
            var profName = beam.ProfName;
            var type = ProfilesManager.GetProfileType(new ProfileName(profName));
            Log.Write($"Handrail.Perfil {label}: ProfName='{profName}' tipo={type?.GetType().Name ?? "null"}");
            if (type == null) return dims;

            // 1) Parametros genericos (os nomes variam por familia de perfil - o log mostra todos)
            try
            {
                foreach (var kv in type.GetParameters())
                {
                    if (TryNumber(kv.Value, out var n)) dims._values[kv.Key] = n;
                }
                Log.Write($"Handrail.Perfil {label}: parametros = " +
                          string.Join(", ", dims._values.Select(p => $"{p.Key}={p.Value}")));
            }
            catch (Exception ex)
            {
                Log.Write($"Handrail.Perfil {label}: GetParameters falhou ({ex.GetType().Name}: {ex.Message})");
            }

            // 2) Propriedades tipadas, quando disponiveis (sobrescrevem os genericos)
            switch (type)
            {
                case ProfileTypeI i:
                    dims._values["H"] = i.H; dims._values["B"] = i.B; dims._values["Tw"] = i.Tw; dims._values["Tf"] = i.Tf;
                    break;
                case ProfileTypeR r:
                    dims._values["D"] = r.D; dims._values["T"] = r.T;
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Handrail.Perfil {label}: EXCEPTION {ex}");
        }
        return dims;
    }

    private static bool TryNumber(object? value, out double n)
    {
        switch (value)
        {
            case double d: n = d; return true;
            case float f: n = f; return true;
            case int i: n = i; return true;
            case long l: n = l; return true;
            case short s: n = s; return true;
            default: n = 0; return false;
        }
    }
}
