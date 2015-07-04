﻿using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;

namespace SqlDirectory.Demo
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var connection = new SqlConnection(@"MultipleActiveResultSets=True;Data Source=(localdb)\v11.0;Initial Catalog=TestLucene;Integrated Security=True;Connect Timeout=30;Encrypt=False;TrustServerCertificate=False"))
            {
                connection.Open();
                SqlServerDirectory.ProvisionDatabase(connection, "[search]", true);
            }

            var t1 = Task.Factory.StartNew(Do);
            var t2 = Task.Factory.StartNew(Do);
            var t3 = Task.Factory.StartNew(Do);

            Task.WaitAll(t1, t2, t3);
        }

        static void LockCanBeReleased()
        {
            using (var connection = new SqlConnection(@"MultipleActiveResultSets=True;Data Source=(localdb)\v11.0;Initial Catalog=TestLucene;Integrated Security=True;Connect Timeout=30;Encrypt=False;TrustServerCertificate=False"))
            {
                connection.Open();
                var directory = new SqlServerDirectory(connection, new Options() { SchemaName = "[search]", LockTimeoutInMinutes = 1 });

                new IndexWriter(directory, new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_30),
                            !IndexReader.IndexExists(directory),
                            new Lucene.Net.Index.IndexWriter.MaxFieldLength(IndexWriter.DEFAULT_MAX_FIELD_LENGTH));

                IndexWriter indexWriter = null;
                while (indexWriter == null)
                {
                    try
                    {
                        indexWriter = new IndexWriter(directory, new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_30),
                            !IndexReader.IndexExists(directory),
                            new Lucene.Net.Index.IndexWriter.MaxFieldLength(IndexWriter.DEFAULT_MAX_FIELD_LENGTH));
                    }
                    catch (LockObtainFailedException)
                    {
                        Console.WriteLine("Lock is taken, waiting for timeout...{0}", DateTime.Now);
                        Thread.Sleep(1000);
                    }
                }
            }
        }

        static void Do()
        {
            //var directory = new SimpleFSDirectory(new DirectoryInfo(@"c:\temp\lucene"));
            using (var connection = new SqlConnection(@"MultipleActiveResultSets=True;Data Source=(localdb)\v11.0;Initial Catalog=TestLucene;Integrated Security=True;Connect Timeout=30;Encrypt=False;TrustServerCertificate=False"))
            {
                connection.Open();
                var directory = new SqlServerDirectory(connection, new Options() { SchemaName = "[search]" });

                IndexWriter indexWriter = null;
                while (indexWriter == null)
                {
                    try
                    {
                        indexWriter = new IndexWriter(directory, new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_30),
                            !IndexReader.IndexExists(directory),
                            new Lucene.Net.Index.IndexWriter.MaxFieldLength(IndexWriter.DEFAULT_MAX_FIELD_LENGTH));
                    }
                    catch (LockObtainFailedException)
                    {
                        Console.WriteLine("Lock is taken, waiting for timeout...");
                        Thread.Sleep(1000);
                    }
                }
                ;
                Console.WriteLine("IndexWriter lock obtained, this process has exclusive write access to index");
                indexWriter.SetRAMBufferSizeMB(100.0);
                indexWriter.SetInfoStream(new StreamWriter(Console.OpenStandardOutput()));
                indexWriter.UseCompoundFile = false;

                for (int iDoc = 0; iDoc < 1000; iDoc++)
                {
                    if (iDoc % 10 == 0)
                        Console.WriteLine(iDoc);
                    Document doc = new Document();
                    doc.Add(new Field("id", DateTime.Now.ToFileTimeUtc().ToString(), Field.Store.YES,
                        Field.Index.ANALYZED, Field.TermVector.NO));
                    doc.Add(new Field("Title", "dog " + GeneratePhrase(50), Field.Store.NO, Field.Index.ANALYZED,
                        Field.TermVector.NO));
                    doc.Add(new Field("Body", "dog " + GeneratePhrase(50), Field.Store.NO, Field.Index.ANALYZED,
                        Field.TermVector.NO));
                    indexWriter.AddDocument(doc);
                }

                Console.Write("Flushing and disposing writer...");
                indexWriter.Flush(true, true, true);
                indexWriter.Dispose();

                Console.WriteLine("Total docs is {0}", indexWriter.NumDocs());

                IndexSearcher searcher;

                using (new AutoStopWatch("Creating searcher"))
                {
                    searcher = new IndexSearcher(directory);
                }
                using (new AutoStopWatch("Count"))
                    Console.WriteLine("Number of docs: {0}", searcher.IndexReader.NumDocs());

                while (true)
                {
                    SearchForPhrase(searcher, "microsoft");
                    Thread.Sleep(1000);
                    //Console.WriteLine("Press a key to search again");
                    //Console.ReadKey();
                }
            }
        }


        static void SearchForPhrase(IndexSearcher searcher, string phrase)
        {
            using (new AutoStopWatch($"Search for {phrase}"))
            {
                Lucene.Net.QueryParsers.QueryParser parser = new Lucene.Net.QueryParsers.QueryParser(Lucene.Net.Util.Version.LUCENE_30, "Body", new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_30));
                Lucene.Net.Search.Query query = parser.Parse(phrase);

                var hits = searcher.Search(query, 100);
                Console.WriteLine("Found {0} results for {1}", hits.TotalHits, phrase);
            }
        }

        static readonly Random Random = new Random((int)DateTime.Now.Ticks);
        static readonly string[] SampleTerms =
            {
                "dog","cat","car","horse","door","tree","chair","microsoft","apple","adobe","google","golf","linux","windows","firefox","mouse","hornet","monkey","giraffe","computer","monitor",
                "steve","fred","lili","albert","tom","shane","gerald","chris",
                "love","hate","scared","fast","slow","new","old"
            };

        private static string GeneratePhrase(int MaxTerms)
        {
            StringBuilder phrase = new StringBuilder();
            int nWords = 2 + Random.Next(MaxTerms);
            for (int i = 0; i < nWords; i++)
            {
                phrase.AppendFormat(" {0} {1}", SampleTerms[Random.Next(SampleTerms.Length)], Random.Next(32768).ToString());
            }
            return phrase.ToString();
        }

    }
    public class AutoStopWatch : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly string _message;
        public AutoStopWatch(string message)
        {
            _message = message;
            Console.WriteLine("{0} starting ", message);
            _stopwatch = Stopwatch.StartNew();
        }


        #region IDisposable Members
        public void Dispose()
        {

            _stopwatch.Stop();
            long ms = _stopwatch.ElapsedMilliseconds;

            Console.WriteLine("{0} Finished {1} ms", _message, ms);
        }
        #endregion
    }
}
