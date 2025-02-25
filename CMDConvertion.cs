using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;

namespace DBSqlUpdate
{
    public delegate void EventProgress(EventConvertProgressArgs e);


    public class EventConvertProgressArgs : EventArgs
    {
        public int ProgressValue = 0;
        public string Description = "";

        public EventConvertProgressArgs Increment(int inc)
        {
            ProgressValue += inc;
            return this;
        }
    }

    /// <summary>
    /// Piotr Kuliński (c) 2024 <br/>
    /// Główny moduł ruchomienia konwersji bazy na podstaiwe dostarczonego modelu XML
    /// </summary>
    public class CMDConvertion
    {
        public event EventProgress OnProgress;

        private DBModel DBDestination = null;
        private DBModel Pattern = null;

        public CMDConvertion()
        {
            DBDestination = new DBModel();

            HelperDBStructureXML cmd = new HelperDBStructureXML(Program.connection);
            cmd.Procedures =
            cmd.Functions =
            cmd.Triggers =
            cmd.Scripts =
            cmd.Views =
            cmd.Tables =
            cmd.Permission = 1;
            cmd.GetDBModel(ref DBDestination);

            Pattern = Program.Pattern;

            Program.connection.Dbm = DBDestination;
        }

        public void Excecute()
        {
            EventConvertProgressArgs progress = new EventConvertProgressArgs();
            DateTime startTime = DateTime.Now;

            DBDestination.CompareStructures(Pattern);

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingScript -= GKUEventHandler.OnProcessingScriptDefault;
            Pattern.OnProcessingScript += Program.connection.OnProcessingScriptPrev;
            Pattern.ProcessingScripts();
            Pattern.OnProcessingScript -= Program.connection.OnProcessingScriptPrev;

            OnProgress?.Invoke(progress.Increment(4));
            DBDestination.OnProcessingFP += Program.connection.OnProcessingFPDrop;
            DBDestination.ProcessingTriggers();
            DBDestination.OnProcessingFP -= Program.connection.OnProcessingFPDrop;

            OnProgress?.Invoke(progress.Increment(4));
            DBDestination.OnProcessingFP += Program.connection.OnProcessingFPDrop;
            DBDestination.ProcessingProgrammability();
            DBDestination.OnProcessingFP -= Program.connection.OnProcessingFPDrop;

            OnProgress?.Invoke(progress.Increment(4));
            DBDestination.OnProcessingKey += Program.connection.DropForeignKey;
            DBDestination.ProcessingKeys();
            DBDestination.OnProcessingKey -= Program.connection.DropForeignKey;

            OnProgress?.Invoke(progress.Increment(4));
            DBDestination.OnProcessingConstraint += Program.connection.DropCheckConstratinColumn;
            DBDestination.ProcessingConstraints();
            DBDestination.OnProcessingConstraint -= Program.connection.DropCheckConstratinColumn;

            OnProgress?.Invoke(progress.Increment(4));
            DBDestination.OnProcessingIndex += Program.connection.DropIndex;
            DBDestination.ProcessingIndexes();
            DBDestination.OnProcessingIndex -= Program.connection.DropIndex;

            OnProgress?.Invoke(progress.Increment(4));
            DBDestination.OnProcessingSynonim += Program.connection.OnDeleteSynonim;
            DBDestination.ProcessingSynonims();
            DBDestination.OnProcessingSynonim -= Program.connection.OnDeleteSynonim;

            OnProgress?.Invoke(progress.Increment(4));
            DBDestination.OnProcessingXMLSchema += Program.connection.OnDeleteXMLSchema;
            DBDestination.ProcessingXMLSchema();
            DBDestination.OnProcessingXMLSchema -= Program.connection.OnDeleteXMLSchema;

            OnProgress?.Invoke(progress.Increment(4));
            DBDestination.OnProcessingColumn += Program.connection.DropDefaultConstratinColumn;
            DBDestination.OnProcessingColumn += Program.connection.DropComputetdColumn;
            DBDestination.ProcessingColumns();
            DBDestination.OnProcessingColumn -= Program.connection.DropDefaultConstratinColumn;
            DBDestination.OnProcessingColumn -= Program.connection.DropComputetdColumn;

            //zdjęcie typów tabelarycznych
            OnProgress?.Invoke(progress.Increment(4));
            DBDestination.OnProcessingTableTypes += Program.connection.DropTableType;
            DBDestination.ProcessingTableTypes();
            DBDestination.OnProcessingTableTypes -= Program.connection.DropTableType;

            OnProgress?.Invoke(progress.Increment(4));
            DBDestination.MargeTablesStructure(Pattern);

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingTable += Program.connection.CreateTable;
            Pattern.ProcessingTables();
            Pattern.OnProcessingTable -= Program.connection.CreateTable;

            //utworzenie typów tabelarycznych
            Pattern.OnProcessingTableTypes += Program.connection.CreateTableType;
            Pattern.ProcessingTableTypes();
            Pattern.OnProcessingTableTypes -= Program.connection.CreateTableType;


            // Usuwanie tabel których nie ma w modelu - potencjalnie niebezpieczne, 
            // gdybyśmy dostarczali jedynie upgrade byłby problem
            if (Program.config.Parameters != null && Program.config.Parameters.DropUndefinedTable)
            {
                DBDestination.Tables.ForEach( (table) =>
                {
                    if (table.State == EnumState.IsForDelete)
                    {
                        Helpers.RegisterMessage(String.Format("Tabela do usunięcia [{0}].[{1}]", table.schema_name, table.name));
                        Program.connection.DropTable(DBDestination, table);
                    }
                });
            }

            OnProgress?.Invoke(progress.Increment(4));
            DBDestination.OnProcessingColumn += Program.connection.DropColumn;
            DBDestination.ProcessingColumns();
            DBDestination.OnProcessingColumn -= Program.connection.DropColumn;

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingColumn += Program.connection.AddColumn;
            Pattern.OnProcessingColumn += Program.connection.AlterColumn;
            Pattern.ProcessingColumns();
            Pattern.OnProcessingColumn -= Program.connection.AddColumn;
            Pattern.OnProcessingColumn -= Program.connection.AlterColumn;

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingSchema += Program.connection.MakeSchema;
            Pattern.ProcessingSchemas();


            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingXMLSchema += Program.connection.OnCreateXMLSchema;
            Pattern.ProcessingXMLSchema();
            Pattern.OnProcessingXMLSchema -= Program.connection.OnCreateXMLSchema;

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingColumn += Program.connection.AddDefaultConstratinColumn;
            Pattern.OnProcessingColumn += Program.connection.AddComputetdColumn;
            Pattern.ProcessingColumns();
            Pattern.OnProcessingColumn -= Program.connection.AddDefaultConstratinColumn;
            Pattern.OnProcessingColumn -= Program.connection.AddComputetdColumn;

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingConstraint += Program.connection.AddCheckConstratinColumn;
            Pattern.ProcessingConstraints();
            Pattern.OnProcessingConstraint -= Program.connection.AddCheckConstratinColumn;

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingIndex += Program.connection.AddPrimaryKey;
            Pattern.ProcessingIndexes();
            Pattern.OnProcessingIndex -= Program.connection.AddPrimaryKey;

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingIndex += Program.connection.AddIndexUnique;
            Pattern.ProcessingIndexesUnique();
            Pattern.OnProcessingIndex -= Program.connection.AddIndexUnique;

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingKey += Program.connection.AddForeignKey;
            Pattern.ProcessingKeys();
            Pattern.OnProcessingKey -= Program.connection.AddForeignKey;

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingIndex += Program.connection.AddIndex;
            Pattern.ProcessingIndexes();
            Pattern.OnProcessingIndex += Program.connection.AddIndex;

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingFP += Program.connection.OnProcessingFPCreate;
            Pattern.ProcessingProgrammability();
            Pattern.OnProcessingFP -= Program.connection.OnProcessingFPCreate; 

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingFP += Program.connection.OnProcessingFPCreate;
            Pattern.ProcessingTriggers();
            Pattern.OnProcessingFP -= Program.connection.OnProcessingFPCreate;

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingSynonim += Program.connection.MakeSynonim;
            Pattern.ProcessingSynonims();

            //przywrócenie niestandardowych obiektów
            //DBDestination.Programmability.Procedures.lista.ForEach( (proc) =>
            //{
            //     if (proc.State == EnumState.IsForDelete)
            //     {
            //         Helpers.RegisterMessage(String.Format("Przywracam niestandardową procedurę {0}",  proc.name));
            //         Program.connection.OnProcessingFPCreate(proc);
            //     }
            //});
            if (DBDestination.Permissions != null && DBDestination.Permissions.User != null)
            {
                DBDestination.Permissions.User.ForEach((perm) =>
                {
                    Helpers.RegisterMessage(String.Format("Aktualizacja uprawnień dla użytkownika [{0}]", perm.name));
                    Program.connection.ExecuteCommand(perm);
                    perm.State = EnumState.IsComplet;
                });
            }

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingScript += Program.connection.OnProcessingScriptPost;
            Pattern.ProcessingScripts();

            OnProgress?.Invoke(progress.Increment(4));
            Pattern.OnProcessingInfo += Program.connection.SaveVersion;
            Pattern.ProcessingInfo();

            progress.ProgressValue = 100;
            OnProgress?.Invoke(progress);
            TimeSpan roznica = DateTime.Now - startTime;
            Helpers.RegisterMessage(String.Format("czas: {0} godz, {1} min, {2} sek", roznica.Hours, roznica.Minutes, roznica.Seconds));
        }

        public void ExecuteScript()
        {
            Helpers.RegisterMessage("Wykonanie skryptu: " + Program.config.RunScript);
            if (Program.config.RunScript == ScriptType.Post.ToString())
                Pattern.OnProcessingScript += Program.connection.OnProcessingScriptPost;
            else
                Pattern.OnProcessingScript += Program.connection.OnProcessingScriptPrev;

            Pattern.ProcessingScripts();
        }
    }
}
